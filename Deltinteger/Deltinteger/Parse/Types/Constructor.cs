using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;

namespace Deltin.Deltinteger.Parse
{
    public class Constructor : IParameterCallable, ICallable
    {
        public string Name => Type.Name;
        public AccessLevel AccessLevel { get; }
        public CodeParameter[] Parameters { get; protected set; }
        public LanguageServer.Location DefinedAt { get; }
        public CodeType Type { get; }
        public string Documentation { get; protected set; }

        public Constructor(CodeType type, LanguageServer.Location definedAt, AccessLevel accessLevel)
        {
            Type = type;
            DefinedAt = definedAt;
            AccessLevel = accessLevel;
            Parameters = new CodeParameter[0];
        }

        public virtual void Parse(ActionSet actionSet, IWorkshopTree[] parameterValues, object[] additionalParameterData) {}

        public void Call(ParseInfo parseInfo, DocRange callRange)
        {
            if (DefinedAt == null) return;
            
            parseInfo.Script.AddDefinitionLink(callRange, DefinedAt);
            if (Type is DefinedType)
                ((DefinedType)Type).AddLink(new LanguageServer.Location(parseInfo.Script.Uri, callRange));
        }

        public string GetLabel(bool markdown) => HoverHandler.GetLabel("new " + Type.Name, Parameters, markdown, Documentation);
    }

    public class DefinedConstructor : Constructor, IApplyBlock
    {
        public Var[] ParameterVars { get; private set; }
        public Scope ConstructorScope { get; }
        public BlockAction Block { get; private set; }

        private ParseInfo parseInfo { get; }
        private DeltinScriptParser.ConstructorContext context { get; }

        public CallInfo CallInfo { get; }

        public DefinedConstructor(ParseInfo parseInfo, Scope scope, CodeType type, DeltinScriptParser.ConstructorContext context) : base(
            type,
            new LanguageServer.Location(parseInfo.Script.Uri, DocRange.GetRange(context.name)),
            context.accessor().GetAccessLevel())
        {
            this.parseInfo = parseInfo;
            this.context = context;
            CallInfo = new CallInfo(this, parseInfo.Script);

            ConstructorScope = scope.Child();

            if (Type is DefinedType)
                ((DefinedType)Type).AddLink(DefinedAt);
            
            parseInfo.TranslateInfo.ApplyBlock(this);
            parseInfo.TranslateInfo.GetComponent<SymbolLinkComponent>().AddSymbolLink(this, DefinedAt, true);
            parseInfo.Script.AddCodeLensRange(new ReferenceCodeLensRange(this, parseInfo, CodeLensSourceType.Constructor, DefinedAt.range));
        }

        public void SetupParameters()
        {
            var parameterInfo = CodeParameter.GetParameters(parseInfo, ConstructorScope, context.setParameters(), false);
            Parameters = parameterInfo.Parameters;
            ParameterVars = parameterInfo.Variables;

            parseInfo.Script.AddHover(DocRange.GetRange(context.name), GetLabel(true));
        }

        public void SetupBlock()
        {
            Block = new BlockAction(parseInfo.SetCallInfo(CallInfo), ConstructorScope, context.block());
            foreach (var listener in listeners) listener.Applied();
        }

        public override void Parse(ActionSet actionSet, IWorkshopTree[] parameterValues, object[] additionalParameterData)
        {
            actionSet = actionSet.New(actionSet.IndexAssigner.CreateContained()).PackThis();
            DefinedMethod.AssignParameters(actionSet, ParameterVars, parameterValues);
            Block.Translate(actionSet);
        }

        private List<IOnBlockApplied> listeners = new List<IOnBlockApplied>();
        public void OnBlockApply(IOnBlockApplied onBlockApplied)
        {
            listeners.Add(onBlockApplied);
        }
    }
}