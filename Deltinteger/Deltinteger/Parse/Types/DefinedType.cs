using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

namespace Deltin.Deltinteger.Parse
{
    public class DefinedType : CodeType
    {
        public LanguageServer.Location DefinedAt { get; }
        private readonly List<ObjectVariable> objectVariables = new List<ObjectVariable>();
        private Scope scope;
        private ParseInfo parseInfo;
        private DeltinScriptParser.Type_defineContext typeContext;
        private List<IApplyBlock> applyBlocks;

        public DefinedType(ParseInfo parseInfo, Scope scope, DeltinScriptParser.Type_defineContext typeContext, List<IApplyBlock> applyBlocks) : base(typeContext.name.Text)
        {
            CanBeDeleted = true;
            this.typeContext = typeContext;
            this.parseInfo = parseInfo;
            this.applyBlocks = applyBlocks;

            if (parseInfo.TranslateInfo.IsCodeType(Name))
                parseInfo.Script.Diagnostics.Error($"A type with the name '{Name}' already exists.", DocRange.GetRange(typeContext.name));
            
            DefinedAt = new LanguageServer.Location(parseInfo.Script.Uri, DocRange.GetRange(typeContext.name));
            parseInfo.TranslateInfo.AddSymbolLink(this, DefinedAt);

            // Get the constructors.
            if (typeContext.constructor().Length > 0)
            {
                Constructors = new Constructor[typeContext.constructor().Length];
                for (int i = 0; i < Constructors.Length; i++)
                {
                    Constructors[i] = new DefinedConstructor(parseInfo, this, typeContext.constructor(i));
                    applyBlocks.Add((DefinedConstructor)Constructors[i]);
                }
            }
            else
            {
                // If there are no constructors, create a default constructor.
                Constructors = new Constructor[] {
                    new Constructor(this, new Location(parseInfo.Script.Uri, DocRange.GetRange(typeContext.name)), AccessLevel.Public)
                };
            }
        }

        public void ResolveElements()
        {
            // Get the type being extended.
            if (typeContext.TERNARY_ELSE() != null)
            {
                // If there is no type name, error.
                if (typeContext.extends == null)
                    parseInfo.Script.Diagnostics.Error("Expected type name.", DocRange.GetRange(typeContext.TERNARY_ELSE()));
                else
                {
                    // Get the type being inherited.
                    CodeType inheriting = parseInfo.TranslateInfo.GetCodeType(typeContext.extends.Text, parseInfo.Script.Diagnostics, DocRange.GetRange(typeContext.extends));

                    // GetCodeType will return null if the type is not found.
                    if (inheriting != null)
                    {
                        inheriting.Call(parseInfo.Script, DocRange.GetRange(typeContext.extends));
                        Inherit(inheriting, parseInfo.Script.Diagnostics, DocRange.GetRange(typeContext.extends));
                    }
                }
            }

            if (Extends == null)
            {
                scope = parseInfo.TranslateInfo.GlobalScope.Child("class " + Name);
                scope.ProtectedCatch = true;
            }
            else
                scope = Extends.ReturningScope().Child("class " + Name);
            scope.PrivateCatch = true;

            // Todo: Static methods/macros.
            foreach (var definedMethod in typeContext.define_method())
            {
                var newMethod = new DefinedMethod(parseInfo, scope, definedMethod, this);
                applyBlocks.Add(newMethod);
            }

            foreach (var macroContext in typeContext.define_macro())
            {
                DeltinScript.GetMacro(parseInfo, scope, macroContext, applyBlocks);
            }

            // Get the variables defined in the type.
            foreach (var definedVariable in typeContext.define())
            {
                // TODO: Move Finalize
                Var newVar = Var.CreateVarFromContext(VariableDefineType.InClass, parseInfo, definedVariable);
                newVar.Finalize(scope);
                if (!newVar.Static) objectVariables.Add(new ObjectVariable(newVar));
            }
        }

        private int StackStart(bool inclusive)
        {
            int extStack = 0;
            if (Extends != null) extStack = ((DefinedType)Extends).StackStart(true);
            if (inclusive) extStack += objectVariables.Count;
            return extStack;
        }

        public override void WorkshopInit(DeltinScript translateInfo)
        {
            ClassData classData = translateInfo.SetupClasses();
            int stackOffset = StackStart(false);

            for (int i = 0; i < objectVariables.Count; i++)
                objectVariables[i].SetArrayStore(classData.GetClassVariableStack(translateInfo.VarCollection, i + stackOffset));
        }
        
        override public Scope ReturningScope()
        {
            return scope;
        }

        override public IWorkshopTree New(ActionSet actionSet, Constructor constructor, IWorkshopTree[] constructorValues, object[] additionalParameterData)
        {
            actionSet = actionSet.New(actionSet.IndexAssigner.CreateContained());

            var classData = actionSet.Translate.DeltinScript.SetupClasses();
            
            // Classes are stored in the class array (`classData.ClassArray`),
            // this stores the index where the new class is created at.
            var classReference = actionSet.VarCollection.Assign("_new_" + Name + "_class_index", actionSet.IsGlobal, true);
            classData.GetClassIndex(classReference, actionSet);
            
            // Run the constructor.
            BaseSetup(actionSet, (Element)classReference.GetVariable());
            AddObjectVariablesToAssigner((Element)classReference.GetVariable(), actionSet.IndexAssigner);
            constructor.Parse(actionSet.New((Element)classReference.GetVariable()), constructorValues, null);

            return classReference.GetVariable();
        }

        public override void BaseSetup(ActionSet actionSet, Element reference)
        {
            if (Extends != null)
                Extends.BaseSetup(actionSet, reference);

            foreach (ObjectVariable variable in objectVariables)
            if (variable.Variable.InitialValue != null)
            {
                actionSet.AddAction(variable.ArrayStore.SetVariable(
                    value: (Element)variable.Variable.InitialValue.Parse(actionSet),
                    index: reference
                ));
            }
        }

        /// <summary>
        /// Adds the class objects to the index assigner.
        /// </summary>
        /// <param name="source">The source of the type.</param>
        /// <param name="assigner">The assigner that the object variables will be added to.</param>
        public override void AddObjectVariablesToAssigner(IWorkshopTree reference, VarIndexAssigner assigner)
        {
            Extends?.AddObjectVariablesToAssigner(reference, assigner);
            for (int i = 0; i < objectVariables.Count; i++)
                objectVariables[i].AddToAssigner((Element)reference, assigner);
        }

        /// <summary>
        /// Deletes a variable from memory.
        /// </summary>
        /// <param name="actionSet">The action set to add the actions to.</param>
        /// <param name="reference">The object reference.</param>
        public override void Delete(ActionSet actionSet, Element reference)
        {
            if (Extends != null && Extends.CanBeDeleted)
                Extends.Delete(actionSet, reference);

            foreach (ObjectVariable objectVariable in objectVariables)
                actionSet.AddAction(objectVariable.ArrayStore.SetVariable(
                    value: new V_Number(0),
                    index: reference
                ));
        }

        public override void Call(ScriptFile script, DocRange callRange)
        {
            base.Call(script, callRange);
            script.AddDefinitionLink(callRange, DefinedAt);
            AddLink(new LanguageServer.Location(script.Uri, callRange));
        }
        public void AddLink(LanguageServer.Location location)
        {
            parseInfo.TranslateInfo.AddSymbolLink(this, location);
        }

        override public CompletionItem GetCompletion()
        {
            return new CompletionItem()
            {
                Label = Name,
                Kind = CompletionItemKind.Class
            };
        }
    }

    class ObjectVariable
    {
        public Var Variable { get; }
        public IndexReference ArrayStore { get; private set; }

        public ObjectVariable(Var variable)
        {
            Variable = variable;
        }

        public void SetArrayStore(IndexReference store)
        {
            ArrayStore = store;
        }

        public void AddToAssigner(Element reference, VarIndexAssigner assigner)
        {
            assigner.Add(Variable, ArrayStore.CreateChild(reference));
        }
    }
}