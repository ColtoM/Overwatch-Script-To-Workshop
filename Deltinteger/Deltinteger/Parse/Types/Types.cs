using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StringOrMarkupContent = OmniSharp.Extensions.LanguageServer.Protocol.Models.StringOrMarkupContent;

namespace Deltin.Deltinteger.Parse
{
    public abstract class CodeType : IExpression, ICallable
    {
        public string Name { get; }
        public Constructor[] Constructors { get; protected set; } = new Constructor[0];
        public CodeType Extends { get; private set; }
        public string Description { get; protected set; }

        /// <summary>Determines if the class can be deleted with the delete keyword.</summary>
        public bool CanBeDeleted { get; protected set; } = false;

        /// <summary>Determines if other classes can inherit this class.</summary>
        public bool CanBeExtended { get; private set; } = false;

        /// <summary>Should be true if the class was called in the script.</summary>
        public bool ShouldInit { get; private set; } = false;

        public CodeType(string name)
        {
            Name = name;
        }

        protected void Inherit(CodeType extend, FileDiagnostics diagnostics, DocRange range)
        {
            if (extend == null) throw new ArgumentNullException(nameof(extend));

            if (extend.CanBeExtended)
            {
                string errorMessage = "Type '" + extend.Name + "' cannot be inherited.";

                if (diagnostics == null || range == null) throw new Exception(errorMessage);
                else
                {
                    diagnostics.Error(errorMessage, range);
                    return;
                }
            }

            Extends = extend;
        }

        public virtual Scope ReturningScope() => null;
        public virtual Scope GetObjectScope()
        {
            return ReturningScope()?.Child(true);
        }

        public CodeType Type() => null;
        public IWorkshopTree Parse(ActionSet actionSet, bool asElement = true) => null;

        /// <summary>
        /// Determines if variables with this type can have their value changed.
        /// </summary>
        public virtual TypeSettable Constant() => TypeSettable.Normal;

        public virtual IWorkshopTree New(ActionSet actionSet, Constructor constructor, IWorkshopTree[] constructorValues, object[] additionalParameterData)
        {
            // Classes that can't be created shouldn't have constructors.
            throw new NotImplementedException();
        }
        public virtual void BaseSetup(ActionSet actionSet, Element reference) { throw new NotImplementedException(); }

        public virtual void WorkshopInit(DeltinScript translateInfo) {}
        public virtual void AddObjectVariablesToAssigner(IWorkshopTree reference, VarIndexAssigner assigner) {}

        public virtual void Delete(ActionSet actionSet, Element reference) {}

        public virtual void Call(ScriptFile script, DocRange callRange)
        {
            script.AddHover(callRange, HoverHandler.Sectioned("class " + Name, Description));
        }

        public abstract CompletionItem GetCompletion();

        public static CodeType GetCodeTypeFromContext(ParseInfo parseInfo, DeltinScriptParser.Code_typeContext typeContext)
        {
            if (typeContext == null) return null;
            CodeType type = parseInfo.TranslateInfo.GetCodeType(typeContext.PART().GetText(), parseInfo.Script.Diagnostics, DocRange.GetRange(typeContext));

            if (type != null)
            {
                type.Call(parseInfo.Script, DocRange.GetRange(typeContext));

                if (typeContext.INDEX_START() != null)
                    for (int i = 0; i < typeContext.INDEX_START().Length; i++)
                        type = new ArrayType(type);
            }
            return type;
        }

        public static bool TypeMatches(CodeType parameterType, CodeType valueType)
        {
            return parameterType == null || parameterType.Name == valueType?.Name;
        }

        static List<CodeType> _defaultTypes;
        public static List<CodeType> DefaultTypes {
            get {
                if (_defaultTypes == null) GetDefaultTypes();
                return _defaultTypes;
            }
        }
        private static void GetDefaultTypes()
        {
            _defaultTypes = new List<CodeType>();
            foreach (var enumData in EnumData.GetEnumData())
                _defaultTypes.Add(new WorkshopEnumType(enumData));
            
            // Add custom classes here.
            _defaultTypes.Add(new Pathfinder.PathmapClass());
            _defaultTypes.Add(new Models.AssetClass());
        }
    }

    public enum TypeSettable
    {
        Normal, Convertable, Constant
    }

    public class ArrayType : CodeType
    {
        public CodeType ArrayOfType { get; }

        public ArrayType(CodeType arrayOfType) : base(arrayOfType.Name + "[]")
        {
            ArrayOfType = arrayOfType;
        }

        public override Scope ReturningScope() => null;
        public override CompletionItem GetCompletion() => throw new NotImplementedException();
    }
}