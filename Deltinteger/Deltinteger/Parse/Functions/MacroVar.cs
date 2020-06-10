using System;
using System.Collections.Generic;
using Deltin.Deltinteger.LanguageServer;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

namespace Deltin.Deltinteger.Parse
{
    public class MacroVar : IVariable, IExpression, ICallable, IApplyBlock
    {
        public string Name { get; }
        public AccessLevel AccessLevel { get; }
        public LanguageServer.Location DefinedAt { get; }
        public bool Static { get; }
        public bool WholeContext => true;

        public IExpression Expression { get; private set; }
        public CodeType ReturnType { get; private set; }

        private DeltinScriptParser.ExprContext ExpressionToParse { get; }
        private Scope scope { get; }
        private ParseInfo parseInfo { get; }

        public CallInfo CallInfo { get; }

        public MacroVar(ParseInfo parseInfo, Scope objectScope, Scope staticScope, DeltinScriptParser.Define_macroContext macroContext, CodeType returnType)
        {
            Name = macroContext.name.Text;
            AccessLevel = macroContext.accessor().GetAccessLevel();
            DefinedAt = new Location(parseInfo.Script.Uri, DocRange.GetRange(macroContext.name));
            CallInfo = new CallInfo(this, parseInfo.Script);
            Static = macroContext.STATIC() != null;

            ReturnType = returnType;
            ExpressionToParse = macroContext.expr();

            scope = Static ? staticScope : objectScope;
            this.parseInfo = parseInfo;

            scope.AddVariable(this, parseInfo.Script.Diagnostics, DocRange.GetRange(macroContext.name));
            parseInfo.TranslateInfo.GetComponent<SymbolLinkComponent>().AddSymbolLink(this, DefinedAt, true);
            parseInfo.Script.AddHover(DocRange.GetRange(macroContext.name), GetLabel(true));
            parseInfo.Script.AddCodeLensRange(new ReferenceCodeLensRange(this, parseInfo, CodeLensSourceType.Variable, DefinedAt.range));
        }

        public void SetupParameters() {}

        public void SetupBlock()
        {
            if (ExpressionToParse != null) Expression = parseInfo.SetCallInfo(CallInfo).GetExpression(scope, ExpressionToParse);
            foreach (var listener in listeners) listener.Applied();
        }

        public IWorkshopTree Parse(ActionSet actionSet) => Expression.Parse(actionSet);

        public Scope ReturningScope() => ReturnType?.GetObjectScope() ?? parseInfo.TranslateInfo.PlayerVariableScope;

        public CodeType Type() => ReturnType;

        public void Call(ParseInfo parseInfo, DocRange callRange)
        {
            parseInfo.Script.AddDefinitionLink(callRange, DefinedAt);
            parseInfo.Script.AddHover(callRange, GetLabel(true));
            parseInfo.TranslateInfo.GetComponent<SymbolLinkComponent>().AddSymbolLink(this, new Location(parseInfo.Script.Uri, callRange));
        }

        public CompletionItem GetCompletion()
        {
            return new CompletionItem() {
                Label = Name,
                Kind = CompletionItemKind.Property
            };
        }

        public string GetLabel(bool markdown)
        {
            string name = ReturnType?.Name ?? "define" + " " + Name;

            if (markdown) return HoverHandler.Sectioned(name, null);
            else return name;
        }

        private List<IOnBlockApplied> listeners = new List<IOnBlockApplied>();
        public void OnBlockApply(IOnBlockApplied onBlockApplied)
        {
            listeners.Add(onBlockApplied);
        }
    }
}