using System.Text;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.WorkshopWiki;
using Deltin.Deltinteger.Parse;
using Deltin.Deltinteger.LanguageServer;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using SignatureInformation = OmniSharp.Extensions.LanguageServer.Protocol.Models.SignatureInformation;
using StringOrMarkupContent = OmniSharp.Extensions.LanguageServer.Protocol.Models.StringOrMarkupContent;

namespace Deltin.Deltinteger
{
    public interface IWorkshopTree
    {
        string ToWorkshop(OutputLanguage language);
        bool EqualTo(IWorkshopTree other);
        int ElementCount() => 1;
    }

    public interface IMethod : IScopeable, IParameterCallable
    {
        CodeType ReturnType { get; }
        MethodAttributes Attributes { get; }
        IWorkshopTree Parse(ActionSet actionSet, MethodCall methodCall);
        bool DoesReturnValue();
    }

    public interface ISkip
    {
        int SkipParameterIndex();
    }

    public interface INamed
    {
        string Name { get; }
    }

    public interface IScopeable : INamed, IAccessable
    {
        bool Static { get; }
        bool WholeContext { get; }
        CompletionItem GetCompletion();
    }

    public interface IVariable : IScopeable
    {
        bool CanBeIndexed => true;
    }

    public interface ICallable : INamed
    {
        void Call(ParseInfo parseInfo, DocRange callRange);
    }

    public interface IParameterCallable : ILabeled, IAccessable
    {
        CodeParameter[] Parameters { get; }
        string Documentation { get; }
    }

    public interface IAccessable
    {
        Location DefinedAt { get; }
        AccessLevel AccessLevel { get; }
    }

    public interface IGettable
    {
        IWorkshopTree GetVariable(Element eventPlayer = null);
    }

    public interface IIndexReferencer : IVariable, IExpression, ICallable, ILabeled
    {
        bool Settable();
        VariableType VariableType { get; }
    }

    public interface ILabeled
    {
        string GetLabel(bool markdown);
    }

    public interface IApplyBlock : ILabeled
    {
        void SetupParameters();
        void SetupBlock();
        void OnBlockApply(IOnBlockApplied onBlockApplied);
        CallInfo CallInfo { get; }
    }

    public interface IOnBlockApplied
    {
        void Applied();
    }
}