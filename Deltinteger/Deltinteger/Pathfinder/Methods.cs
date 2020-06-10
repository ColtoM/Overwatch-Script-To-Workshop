using System;
using System.IO;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Parse;
using Deltin.Deltinteger.CustomMethods;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StringOrMarkupContent = OmniSharp.Extensions.LanguageServer.Protocol.Models.StringOrMarkupContent;

namespace Deltin.Deltinteger.Pathfinder
{
    // Pathmap object methods
    
    [CustomMethod("Pathfind", "Pathfinds a player.", CustomMethodType.Action, false)]
    class Pathfind : CustomMethodBase
    {
        public override CodeParameter[] Parameters()
        {
            return new CodeParameter[] {
                new CodeParameter("player", "The player to move."),
                new CodeParameter("destination", "the destination to move the player to.")
            };
        }

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            // Store the pathfind destination.
            IndexReference destinationStore = actionSet.VarCollection.Assign("_pathfindDestinationStore", actionSet.IsGlobal, true);
            actionSet.AddAction(destinationStore.SetVariable((Element)parameterValues[1]));

            DijkstraNormal algorithm = new DijkstraNormal(
                actionSet, (Element)actionSet.CurrentObject, Element.Part<V_PositionOf>(parameterValues[0]), (Element)destinationStore.GetVariable()
            );
            algorithm.Get();
            DijkstraBase.Pathfind(
                actionSet, actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>(), (Element)algorithm.finalPath.GetVariable(), (Element)parameterValues[0], (Element)destinationStore.GetVariable()
            );

            return null;
        }
    }

    [CustomMethod("PathfindAll", "Moves an array of players to the specified position by pathfinding.", CustomMethodType.Action, false)]
    class PathfindAll : CustomMethodBase
    {
        public override CodeParameter[] Parameters()
        {
            return new CodeParameter[] {
                new CodeParameter("player", "The array of players to move."),
                new CodeParameter("destination", "The destination to move the players to.")
            };
        }

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            IndexReference destinationStore = actionSet.VarCollection.Assign("_pathfindDestinationStore", actionSet.IsGlobal, true);
            actionSet.AddAction(destinationStore.SetVariable((Element)parameterValues[1]));

            DijkstraMultiSource algorithm = new DijkstraMultiSource(
                actionSet, actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>(), (Element)actionSet.CurrentObject, (Element)parameterValues[0], (Element)destinationStore.GetVariable()
            );
            algorithm.Get();

            return null;
        }
    }

    [CustomMethod("GetPath", "Returns an array of vectors forming a path from the starting point to the destination.", CustomMethodType.MultiAction_Value, false)]
    class GetPath : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("position", "The initial position."),
            new CodeParameter("destination", "The final destination.")
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            IndexReference destinationStore = actionSet.VarCollection.Assign("_pathfindDestinationStore", actionSet.IsGlobal, true);
            actionSet.AddAction(destinationStore.SetVariable((Element)parameterValues[1]));

            DijkstraNormal algorithm = new DijkstraNormal(
                actionSet, (Element)actionSet.CurrentObject, (Element)parameterValues[0], (Element)destinationStore.GetVariable()
            );
            algorithm.Get();

            return Element.Part<V_Append>(algorithm.finalPath.GetVariable(), destinationStore.GetVariable());
        }
    }

    // Pathmap static methods

    [CustomMethod("StopPathfind", "Stops pathfinding for the specified players.", CustomMethodType.Action, false)]
    class StopPathfind : CustomMethodBase
    {
        public override CodeParameter[] Parameters()
        {
            return new CodeParameter[] {
                new CodeParameter("players", "The players that will stop pathfinding. Can be a single player or an array of players.")
            };
        }

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            actionSet.AddAction(
                actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>().Path.SetVariable(
                    new V_EmptyArray(), (Element)parameterValues[0]
                )
            );

            return null;
        }
    }

    [CustomMethod("IsPathfinding", "Checks if the target player is currently pathfinding.", CustomMethodType.Value, false)]
    class IsPathfinding : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("players", "The player to check.")
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            return Get(actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>(), (Element)parameterValues[0]);
        }

        public static Element Get(PathfinderInfo info, Element player)
        {
            return new V_Compare(
                Element.Part<V_CountOf>(info.Path.GetVariable(player)),
                Operators.GreaterThan,
                new V_Number(0)
            );
        }
    }

    [CustomMethod("IsPathfindStuck", "Returns true if the specified player takes longer than expected to reach the next pathfind node.", CustomMethodType.Value, false)]
    class IsPathfindStuck : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("player", "The player to check."),
            new CodeParameter(
                "speedScalar",
                "The speed scalar of the player. `1` is the default speed of all heroes except Gengi and Tracer, which is `1.1`. Default value is `1`.",
                new ExpressionOrWorkshopValue(new V_Number(1))
            )
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            PathfinderInfo info = actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>();

            Element leniency = 2;

            Element player = (Element)parameterValues[0];
            Element scalar = (Element)parameterValues[1];
            Element defaultSpeed = 5.5;
            Element nodeDistance = (Element)info.DistanceToNext.GetVariable(player);
            Element timeSinceLastNode = new V_TotalTimeElapsed() - (Element)info.LastUpdate.GetVariable(player);
            
            Element isStuck = new V_Compare(
                nodeDistance - ((defaultSpeed * scalar * timeSinceLastNode) / leniency),
                Operators.LessThanOrEqual,
                new V_Number(0)
            );
            return Element.Part<V_And>(IsPathfinding.Get(info, player), isStuck);
        }
    }

    [CustomMethod("FixPathfind", "Fixes pathfinding for a player by teleporting them to the next node. Use in conjunction with `IsPathfindStuck()`.", CustomMethodType.Action, false)]
    class FixPathfind : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("player", "The player to fix pathfinding for."),
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            Element player = (Element)parameterValues[0];

            actionSet.AddAction(Element.Part<A_Teleport>(
                player,
                actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>().NextPosition(player)
            ));

            return null;
        }
    }

    [CustomMethod("NextNode", "Gets the position of the next node.", CustomMethodType.Value, false)]
    class NextNode : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("player", "The player to get the next node of."),
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            return actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>().NextPosition((Element)parameterValues[0]);
        }
    }

    [CustomMethod("WalkPath", "Input players will walk to each position in the path.", CustomMethodType.Action, false)]
    class WalkPath : CustomMethodBase
    {
        public override CodeParameter[] Parameters() => new CodeParameter[] {
            new CodeParameter("players", "Players that will follow the path."),
            new CodeParameter("path", "The array of positions to walk to.")
        };

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameterValues)
        {
            actionSet.AddAction(actionSet.Translate.DeltinScript.GetComponent<PathfinderInfo>().Path.SetVariable(
                (Element)parameterValues[1],
                (Element)parameterValues[0]
            ));
            return null;
        }
    }
}