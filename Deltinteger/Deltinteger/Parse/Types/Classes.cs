using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Deltin.Deltinteger.Parse
{
    public abstract class ClassType : CodeType
    {
        protected Scope Scope { get; }

        public ClassType(string name) : base(name)
        {
            CanBeDeleted = true;
            Scope = new Scope("class " + name);
        }

        public override IWorkshopTree New(ActionSet actionSet, Constructor constructor, IWorkshopTree[] constructorValues, object[] additionalParameterData)
        {
            // Create the class.
            IndexReference objectReference = actionSet.Translate.DeltinScript.SetupClasses().CreateObject(actionSet, "_new_PathMap");

            New(actionSet, objectReference, constructor, constructorValues, additionalParameterData);

            // Return the reference.
            return objectReference.GetVariable();
        }

        protected virtual void New(ActionSet actionSet, IndexReference objectReference, Constructor constructor, IWorkshopTree[] constructorValues, object[] additionalParameterData)
        {
            // Parse the constructor.
            constructor.Parse(actionSet, constructorValues, additionalParameterData);
        }

        public override Scope ReturningScope() => Scope;

        public override CompletionItem GetCompletion() => new CompletionItem() {
            Label = Name,
            Kind = CompletionItemKind.Class
        };
    }

    public class ClassData
    {
        public IndexReference ClassIndexes { get; }
        private List<IndexReference> VariableStacks { get; } = new List<IndexReference>();

        public ClassData(VarCollection varCollection)
        {
            ClassIndexes = varCollection.Assign("_classIndexes", true, false);
        }

        public IndexReference CreateObject(ActionSet actionSet, string internalName)
        {
            var classReference = actionSet.VarCollection.Assign(internalName, actionSet.IsGlobal, true);
            GetClassIndex(classReference, actionSet);
            return classReference;
        }

        public void GetClassIndex(IndexReference classReference, ActionSet actionSet)
        {
            actionSet.AddAction(classReference.SetVariable(
                Element.Part<V_IndexOfArrayValue>(
                    ClassIndexes.GetVariable(),
                    new V_Number(0)
                )
            ));
            actionSet.AddAction(ClassIndexes.SetVariable(
                1,
                null,
                (Element)classReference.GetVariable()
            ));
        }

        public IndexReference GetClassVariableStack(VarCollection collection, int index)
        {
            if (index > VariableStacks.Count) throw new Exception("Variable stack skipped");
            if (index == VariableStacks.Count)
                VariableStacks.Add(collection.Assign("_objectVariable_" + index, true, false));
            
            return VariableStacks[index];
        }
    }
}