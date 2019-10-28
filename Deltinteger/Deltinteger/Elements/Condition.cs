﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deltin.Deltinteger;

namespace Deltin.Deltinteger.Elements
{
    public class Condition : IWorkshopTree
    {
        public Element Value1 { get; private set; }
        public EnumMember CompareOperator { get; private set; }
        public Element Value2 { get; private set; }

        public Condition(Element value1, EnumMember compareOperator, Element value2)
        {
            if (!value1.ElementData.IsValue)
                throw new ArgumentException("Method in condition must be a value method.", nameof(value1));

            if (!value2.ElementData.IsValue)
                throw new ArgumentException("Method in condition must be a value method.", nameof(value2));

            Value1 = value1;
            CompareOperator = compareOperator;
            Value2 = value2;
        }

        public Condition(Element value1, Elements.Operators compareOperator, Element value2) : this(value1, EnumData.GetEnumValue(compareOperator), value2) {}
        public Condition(V_Compare condition) : this((Element)condition.ParameterValues[0], (EnumMember)condition.ParameterValues[1], (Element)condition.ParameterValues[2]) {}
        public Condition(Element condition) : this(condition, Operators.Equal, new V_True()) {}
        
        public void DebugPrint(Log log, int depth = 0)
        {
            Value1.DebugPrint(log, depth);
            log.Write(LogLevel.Verbose, new ColorMod(new string(' ', depth * 4) + CompareOperator.ToWorkshop(), ConsoleColor.DarkYellow));
            Value2.DebugPrint(log, depth);
        }

        public string ToWorkshop()
        {
            return Value1.ToWorkshop() + " " + CompareOperator.ToWorkshop() + " " + Value2.ToWorkshop();
        }
    }
}
