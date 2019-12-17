using System;
using System.Linq;
using System.Collections.Generic;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Elements;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Deltin.Deltinteger.Parse
{
    public interface IExpression
    {
        Scope ReturningScope();
        CodeType Type();
        IWorkshopTree Parse(ActionSet actionSet);
    }

    public class ExpressionTree : IExpression
    {
        public IExpression[] Tree { get; }
        public IExpression Result { get; }
        public bool Completed { get; } = true;
        public DeltinScriptParser.ExprContext[] ExprContextTree { get; }

        private ITerminalNode _trailingSeperator = null;

        public ExpressionTree(ScriptFile script, DeltinScript translateInfo, Scope scope, DeltinScriptParser.E_expr_treeContext exprContext)
        {
            ExprContextTree = Flatten(script, exprContext);

            Tree = new IExpression[ExprContextTree.Length];
            IExpression current = DeltinScript.GetExpression(script, translateInfo, scope, ExprContextTree[0], false);
            Tree[0] = current;
            if (current != null)
                for (int i = 1; i < ExprContextTree.Length; i++)
                {
                    current = DeltinScript.GetExpression(script, translateInfo, current.ReturningScope() ?? new Scope(), ExprContextTree[i], false);

                    if (current != null && current is IScopeable == false && current is CallMethodAction == false)
                        script.Diagnostics.Error("Expected variable or method.", DocRange.GetRange(ExprContextTree[i]));

                    Tree[i] = current;

                    if (current == null)
                    {
                        Completed = false;
                        break;
                    }
                }
            else Completed = false;
        
            if (Completed)
                Result = Tree[Tree.Length - 1];
            
            // Get the completion items for each expression in the path.
            for (int i = 0; i < Tree.Length; i++)
            if (Tree[i] != null)
            {
                // Get the treescope. Don't get the completion items if it is null.
                var treeScope = Tree[i].ReturningScope();
                if (treeScope != null)
                {
                    Pos start;
                    Pos end;
                    if (i < Tree.Length - 1)
                    {
                        start = DocRange.GetRange(ExprContextTree[i + 1]).start;
                        end = DocRange.GetRange(ExprContextTree[i + 1]).end;
                    }
                    // Expression path has a trailing '.'
                    else if (_trailingSeperator != null)
                    {
                        start = DocRange.GetRange(_trailingSeperator).end;
                        end = DocRange.GetRange(script.NextToken(_trailingSeperator)).start;
                    }
                    else continue;

                    DocRange range = new DocRange(start, end);
                    script.AddCompletionRange(new CompletionRange(treeScope, range, true));
                }
            }
        }

        private DeltinScriptParser.ExprContext[] Flatten(ScriptFile script, DeltinScriptParser.E_expr_treeContext exprContext)
        {
            var exprList = new List<DeltinScriptParser.ExprContext>();
            Flatten(script, exprContext, exprList);
            return exprList.ToArray();
        }

        private void Flatten(ScriptFile script, DeltinScriptParser.E_expr_treeContext exprContext, List<DeltinScriptParser.ExprContext> exprList)
        {            
            if (exprContext.expr(0) is DeltinScriptParser.E_expr_treeContext)
                throw new Exception("Bad list order.");
            
            exprList.Add(exprContext.expr(0));

            if (exprContext.expr().Length == 1)
            {
                script.Diagnostics.Error("Expected expression.", DocRange.GetRange(exprContext.SEPERATOR()));
                _trailingSeperator = exprContext.SEPERATOR();
            }
            else
            {
                if (exprContext.expr(1) is DeltinScriptParser.E_expr_treeContext)
                    Flatten(script, (DeltinScriptParser.E_expr_treeContext)exprContext.expr(1), exprList);
                else
                    exprList.Add(exprContext.expr(1));
            }
        }

        public Scope ReturningScope()
        {
            if (Completed)
                return Result.ReturningScope();
            else
                return null;
        }

        public CodeType Type() => Result.Type();

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            return ParseTree(actionSet).Result;
        }

        public ExpressionTreeParseResult ParseTree(ActionSet actionSet)
        {
            IGettable resultingVariable = null;
            IWorkshopTree target = null;
            IWorkshopTree result = null;
            VarIndexAssigner currentAssigner = actionSet.IndexAssigner;

            for (int i = 0; i < Tree.Length; i++)
            {
                bool isLast = i == Tree.Length - 1;
                IWorkshopTree current;
                if (Tree[i] is Var)
                {
                    var reference = currentAssigner[(Var)Tree[i]];
                    current = reference.GetVariable((Element)target);

                    // If this is the last node in the tree, set the resulting variable.
                    if (isLast) resultingVariable = reference;
                }
                else
                    current = Tree[i].Parse(actionSet);
                
                if (Tree[i].Type() == null)
                {
                    // If this isn't the last in the tree, set it as the target.
                    if (!isLast)
                        target = current;
                }
                else
                {
                    if (Tree[i].Type() is DefinedType)
                    {
                        currentAssigner = actionSet.IndexAssigner.CreateContained();
                        var definedType = ((DefinedType)Tree[i].Type());

                        // Assign the object variables indexes.
                        var source = definedType.GetObjectSource(actionSet.Translate.DeltinScript, current);
                        definedType.AddObjectVariablesToAssigner(source, currentAssigner);
                    }
                    // Implement this if pre-built classes are added.
                    else throw new NotImplementedException();
                }

                result = current;
            }

            if (result == null) throw new Exception("Expression tree result is null");
            return new ExpressionTreeParseResult(result, target, resultingVariable);
        }
    }

    public class ExpressionTreeParseResult
    {
        public IWorkshopTree Result { get; }
        public IWorkshopTree Target { get; }
        public IGettable ResultingVariable { get; }
        public IWorkshopTree[] ResultingIndex { get; }

        public ExpressionTreeParseResult(IWorkshopTree result, IWorkshopTree target, IGettable resultingVariable)
        {
            Result = result;
            Target = target;
            ResultingVariable = resultingVariable;
        }
    }

    public class NumberAction : IExpression
    {
        public double Value { get; }

        public NumberAction(ScriptFile script, DeltinScriptParser.NumberContext numberContext)
        {
            Value = double.Parse(numberContext.GetText());
        }

        public Scope ReturningScope()
        {
            return null;
        }

        public CodeType Type() => null;

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            return new V_Number(Value);
        }
    }

    public class BoolAction : IExpression
    {
        public bool Value { get; }

        public BoolAction(ScriptFile script, bool value)
        {
            Value = value;
        }

        public Scope ReturningScope()
        {
            return null;
        }

        public CodeType Type() => null;

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            if (Value) return new V_True();
            else return new V_False();
        }
    }

    public class NullAction : IExpression
    {
        public NullAction() {}
        public Scope ReturningScope() => null;
        public CodeType Type() => null;

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            return new V_Null();
        }
    }

    public class ValueInArrayAction : IExpression
    {
        public IExpression Expression { get; }
        public IExpression Index { get; }
        private DocRange expressionRange { get; }
        private DocRange indexRange { get; }

        public ValueInArrayAction(ScriptFile script, DeltinScript translateInfo, Scope scope, DeltinScriptParser.E_array_indexContext exprContext)
        {
            Expression = DeltinScript.GetExpression(script, translateInfo, scope, exprContext.array);
            expressionRange = DocRange.GetRange(exprContext.array);

            if (exprContext.index == null)
                script.Diagnostics.Error("Expected an expression.", DocRange.GetRange(exprContext.INDEX_START()));
            else
            {
                Index = DeltinScript.GetExpression(script, translateInfo, scope, exprContext.index);
                indexRange = DocRange.GetRange(exprContext.index);
            }
        }

        public Scope ReturningScope()
        {
            // TODO: Support class arrays.
            return null;
        }

        public CodeType Type() => null;

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            return Element.Part<V_ValueInArray>(Expression.Parse(actionSet.New(expressionRange)), Index.Parse(actionSet.New(indexRange)));
            //return Expression.Parse(actionSet.New(expressionRange))[Index.Parse(actionSet.New(indexRange))];
        }
    }
}