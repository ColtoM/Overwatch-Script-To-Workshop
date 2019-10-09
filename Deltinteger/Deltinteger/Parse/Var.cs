using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Assets.Models;
using Deltin.Deltinteger.Assets.Images;
using Deltin.Deltinteger.Pathfinder;

namespace Deltin.Deltinteger.Parse
{
    public class VarCollection
    {
        private const bool REUSE_VARIABLES = false;

        private Variable Global { get; }
        private Variable Player { get; }

        public WorkshopArrayBuilder WorkshopArrayBuilder { get; }

        public VarCollection(Variable global, Variable player, Variable builder)
        {
            Global = global;
            Player = player;

            IndexedVar tempArrayBuilderVar = AssignVar(null, "Multidimensional Array Builder", true, null);
            WorkshopArrayBuilder = new WorkshopArrayBuilder(builder, tempArrayBuilderVar);
            tempArrayBuilderVar.ArrayBuilder = WorkshopArrayBuilder;
        }

        public int Assign(bool isGlobal)
        {
            if (isGlobal)
            {
                int index = Array.IndexOf(GlobalCollection, null);

                if (index == -1)
                    throw new Exception();
                
                return index;
            }
            else
            {
                int index = Array.IndexOf(PlayerCollection, null);

                if (index == -1)
                    throw new Exception();
                
                return index;
            }
        }

        private void Set(bool isGlobal, IndexedVar var)
        {
            if (var.Index.Length != 1)
                throw new Exception();

            if (isGlobal)
                GlobalCollection[var.CollectionIndex] = var;
            else
                PlayerCollection[var.CollectionIndex] = var;
        }

        public void Free(IndexedVar var)
        {
            # pragma warning disable
            if (!REUSE_VARIABLES)
                return;
            
            if (var.IsGlobal)
            #pragma warning restore
            {
                if (!GlobalCollection.Contains(var))
                    return;

                if (GlobalCollection[var.CollectionIndex] == null)
                    throw new Exception();

                GlobalCollection[var.CollectionIndex] = null;
            }
            else
            {
                if (!PlayerCollection.Contains(var))
                    return;

                if (PlayerCollection[var.CollectionIndex] == null)
                    throw new Exception();

                PlayerCollection[var.CollectionIndex] = null;
            }
        }

        public IndexedVar AssignVar(ScopeGroup scope, string name, bool isGlobal, Node node)
        {
            IndexedVar var;

            if (node == null)
                name = Constants.INTERNAL_ELEMENT + name;

            int collectionIndex = Assign(isGlobal);
            
            if (scope == null || !scope.Recursive)
                var = new IndexedVar  (scope, name, isGlobal, GetUseVar(isGlobal), Element.IntToElement(collectionIndex), WorkshopArrayBuilder, node);
            else
                var = new RecursiveVar(scope, name, isGlobal, GetUseVar(isGlobal), Element.IntToElement(collectionIndex), WorkshopArrayBuilder, node);
            
            var.CollectionIndex = collectionIndex;
            
            Set(isGlobal, var);
            AddVar(var);
            return var;
        }

        public IndexedVar AssignVar(ScopeGroup scope, string name, bool isGlobal, Variable variable, int[] index, Node node)
        {
            IndexedVar var;

            if (scope == null || !scope.Recursive)
                var = new IndexedVar  (scope, name, isGlobal, variable, Element.IntToElement(index), WorkshopArrayBuilder, node);
            else
                var = new RecursiveVar(scope, name, isGlobal, variable, Element.IntToElement(index), WorkshopArrayBuilder, node);

            AddVar(var);
            return var;
        }

        private void AddVar(Var var)
        {
            if (!AllVars.Contains(var))
                AllVars.Add(var);
        }

        private Variable GetUseVar(bool isGlobal)
        {
            return isGlobal ? Global : Player;
        }

        public readonly List<Var> AllVars = new List<Var>();

        private readonly IndexedVar[] GlobalCollection = new IndexedVar[Constants.MAX_ARRAY_LENGTH];
        private readonly IndexedVar[] PlayerCollection = new IndexedVar[Constants.MAX_ARRAY_LENGTH];
    }

    public abstract class Var : IScopeable
    {
        public string Name { get; }
        public ScopeGroup Scope { get; private set; }
        public bool IsDefinedVar { get; }
        public Node Node { get; }

        public DefinedType Type { get; set; }

        public AccessLevel AccessLevel { get; set; } = AccessLevel.Public;

        public Var(string name, ScopeGroup scope, Node node = null)
        {
            Name = name;
            Scope = scope;
            Node = node;
            IsDefinedVar = node != null;

            scope?./* we're */ In(this) /* together! */;
        }

        public abstract Element GetVariable(Element targetPlayer = null);

        public abstract bool Gettable();
        public abstract bool Settable();

        public override string ToString()
        {
            return Name;
        }
    }

    public class IndexedVar : Var
    {
        public WorkshopArrayBuilder ArrayBuilder { get; set; }
        public bool IsGlobal { get; }
        public Variable Variable { get; }
        public Element[] Index { get; }
        public bool UsesIndex { get; }
        public int CollectionIndex { get; set; } = -1;
        public Element DefaultTarget { get; set; } = new V_EventPlayer();
        public bool Optimize2ndDim { get; set; } = false;

        private readonly IWorkshopTree VariableAsWorkshop; 

        public IndexedVar(ScopeGroup scopeGroup, string name, bool isGlobal, Variable variable, Element[] index, WorkshopArrayBuilder arrayBuilder, Node node)
            : base (name, scopeGroup, node)
        {
            IsGlobal = isGlobal;
            Variable = variable;
            VariableAsWorkshop = EnumData.GetEnumValue(Variable);
            Index = index;
            UsesIndex = index != null && index.Length > 0;
            this.ArrayBuilder = arrayBuilder;
        }

        override public bool Gettable() { return true; }
        override public bool Settable() { return true; }

        public override Element GetVariable(Element targetPlayer = null)
        {
            if (targetPlayer == null) targetPlayer = DefaultTarget;
            Element element = Get(targetPlayer);
            if (Type != null)
                element.SupportedType = this;
            return element;
        }

        protected virtual Element Get(Element targetPlayer = null)
        {
            return WorkshopArrayBuilder.GetVariable(IsGlobal, targetPlayer, Variable, Index);
        }

        public virtual Element[] SetVariable(Element value, Element targetPlayer = null, params Element[] setAtIndex)
        {
            return WorkshopArrayBuilder.SetVariable(ArrayBuilder, value, IsGlobal, targetPlayer, Variable, Optimize2ndDim, ArrayBuilder<Element>.Build(Index, setAtIndex));
        }

        public virtual Element[] ModifyVariable(Operation operation, Element value, Element targetPlayer = null, params Element[] setAtIndex)
        {
            return WorkshopArrayBuilder.ModifyVariable(ArrayBuilder, operation, value, IsGlobal, targetPlayer, Variable, ArrayBuilder<Element>.Build(Index, setAtIndex));
        }
        
        public virtual Element[] InScope(Element initialValue, Element targetPlayer = null)
        {
            if (initialValue != null)
                return SetVariable(initialValue, targetPlayer);
            return null;
        }

        public virtual void OutOfScope(TranslateRule context, Element targetPlayer = null)
        {
        }

        public IndexedVar CreateChild(ScopeGroup scope, string name, Element[] index, Node node)
        {
            return new IndexedVar(scope, name, IsGlobal, Variable, ArrayBuilder<Element>.Build(Index, index), ArrayBuilder, node);
        }

        public override string ToString()
        {
            return 
            (IsGlobal ? "global" : "player") + " "
            + Variable + (UsesIndex ? 
                "[" + string.Join(", ", Index.Select(i => i is V_Number ? ((V_Number)i).Value.ToString() : "?")) + "]"
            : "") + " "
            + (AdditionalToStringInfo != null ? AdditionalToStringInfo + " " : "")
            + Name;
        }

        protected virtual string AdditionalToStringInfo { get; } = null;
    }

    class ElementOrigin
    {
        public bool IsGlobal { get; }
        public Element Player { get; }
        public Variable Variable { get; }
        public Element[] Index { get; }

        private ElementOrigin(bool isGlobal, Element player, Variable variable, Element[] index)
        {
            IsGlobal = isGlobal;
            Player = player;
            Variable = variable;
            Index = index;
        }

        public IndexedVar OriginVar(VarCollection varCollection, ScopeGroup scope, string name)
        {
            return new IndexedVar(scope, name, IsGlobal, Variable, Index, varCollection.WorkshopArrayBuilder, null);
        }

        public static ElementOrigin GetElementOrigin(Element element)
        {
            bool isGlobal = false;
            Element player = null;
            Variable variable = Variable.A;

            Element checking = element;
            List<Element> index = new List<Element>();
            while (checking != null)
            {
                if (checking is V_GlobalVariable)
                {
                    isGlobal = true;
                    player = null;
                    variable = (Variable)((EnumMember)checking.ParameterValues[0]).Value;
                    checking = null;
                }
                else if (checking is V_PlayerVariable)
                {
                    isGlobal = false;
                    player = (Element)checking.ParameterValues[0];
                    variable = (Variable)((EnumMember)checking.ParameterValues[1]).Value;
                    checking = null;
                }
                else if (checking is V_ValueInArray)
                {
                    index.Add((Element)checking.ParameterValues[1]);
                    checking = (Element)checking.ParameterValues[0];
                }
                else return null;
            }
            
            return new ElementOrigin(isGlobal, player, variable, index.ToArray());
        }
    }

    public class RecursiveVar : IndexedVar
    {
        public RecursiveVar(ScopeGroup scopeGroup, string name, bool isGlobal, Variable variable, Element[] index, WorkshopArrayBuilder arrayBuilder, Node node)
            : base (scopeGroup, name, isGlobal, variable, index, arrayBuilder, node)
        {
        }

        protected override Element Get(Element targetPlayer = null)
        {
            return Element.Part<V_LastOf>(base.Get(targetPlayer));
        }

        public override Element[] SetVariable(Element value, Element targetPlayer = null, params Element[] setAtIndex)
        {
            return base.SetVariable(value, targetPlayer, CurrentIndex(targetPlayer, setAtIndex));
        }

        public override Element[] ModifyVariable(Operation operation, Element value, Element targetPlayer = null, params Element[] setAtIndex)
        {
            return base.ModifyVariable(operation, value, targetPlayer, CurrentIndex(targetPlayer, setAtIndex));
        }

        private Element[] CurrentIndex(Element targetPlayer, params Element[] setAtIndex)
        {
            return ArrayBuilder<Element>.Build(
                Element.Part<V_CountOf>(base.Get(targetPlayer)) - 1,
                setAtIndex
            );
        }

        public override Element[] InScope(Element initialValue, Element targetPlayer = null)
        {
            return base.SetVariable(initialValue, targetPlayer, Element.Part<V_CountOf>(base.Get(targetPlayer)));
        }

        public override void OutOfScope(TranslateRule context, Element targetPlayer = null)
        {
            Element get = base.Get(targetPlayer);
            context.Actions.AddRange(base.SetVariable(
                Element.Part<V_ArraySlice>(
                    get,
                    new V_Number(0),
                    Element.Part<V_CountOf>(get) - 1
                ),
                targetPlayer
            ));

            base.OutOfScope(context, targetPlayer);
        }

        protected override string AdditionalToStringInfo { get; } = "RECURSIVE";

        public Element DebugStack(Element targetPlayer = null)
        {
            return base.Get(targetPlayer);
        }
    }

    public class ElementReferenceVar : Var
    {
        public IWorkshopTree Reference { get; set; }

        public ElementReferenceVar(string name, ScopeGroup scope, Node node, IWorkshopTree reference) : base (name, scope, node)
        {
            Reference = reference;
        }

        public override Element GetVariable(Element targetPlayer = null)
        {
            if (targetPlayer != null && !(targetPlayer is V_EventPlayer))
                throw new Exception($"{nameof(targetPlayer)} must be null or EventPlayer.");
            
            if (targetPlayer == null)
                targetPlayer = new V_EventPlayer();
            
            if (Reference == null)
                throw new ArgumentNullException(nameof(Reference));

            if (Reference is Element == false)
                throw new Exception("Reference is not an element, can't get the variable.");

            return (Element)Reference;
        }

        override public bool Gettable() { return Reference is Element; }
        override public bool Settable() { return false; }

        public override string ToString()
        {
            return "element reference : " + Name;
        }
    }

    public class ImageVar : Var
    {
        public EffectImage Image { get; }

        public ImageVar(string name, ScopeGroup scope, Node node, EffectImage image) : base(name, scope, node)
        {
            Image = image;
        }

        override public Element GetVariable(Element targetPlayer = null)
        {
            throw new NotImplementedException();
        }

        override public bool Gettable() => false;
        override public bool Settable() => false;
    }

    public class ModelVar : Var
    {
        public Model Model { get; }

        public ModelVar(string name, ScopeGroup scope, Node node, Model model) : base(name, scope, node)
        {
            Model = model;
        }

        override public Element GetVariable(Element targetPlayer = null)
        {
            throw new NotImplementedException();
        }

        override public bool Gettable() => false;
        override public bool Settable() => false;
    }

    public class VarRef : IWorkshopTree
    {
        public Var Var { get; }
        public Element[] Index { get; }
        public Element Target { get; }

        public VarRef(Var var, Element[] index, Element target)
        {
            Var = var;
            Index = index;
            Target = target;
        }

        public string ToWorkshop()
        {
            throw new NotImplementedException();
        }

        public void DebugPrint(Log log, int depth)
        {
            throw new NotImplementedException();
        }

        public double ServerLoadWeight()
        {
            throw new NotImplementedException();
        }
    }
}