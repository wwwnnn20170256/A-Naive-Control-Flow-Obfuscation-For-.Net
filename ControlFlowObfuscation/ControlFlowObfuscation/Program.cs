using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ControlFlowObfuscation
{
    class Program
    {
        static Random rand = new Random(0);

        class Block
        {
            public int index;
            public int rndIndex;
            public int state;
            public readonly List<Instruction> list = new List<Instruction>();
        }

        static List<Block> SplitIntoBlock(MethodDefinition method)
        {
            List<Block> list = new List<Block>();

            int Max = 2;
            int cnt = 0;
            Block block = new Block();
            list.Add(block);
            foreach (var i in method.Body.Instructions)
            {
                cnt += 1;
                block.list.Add(i);

                bool isStore = false;
                var name = i.OpCode.Code.ToString().ToLower();
                if (name.StartsWith("st"))
                {
                    isStore = true;
                }

                if (cnt >= Max && isStore)
                {
                    cnt = 0;
                    block = new Block();
                    list.Add(block);
                }
            }

            if (list.Count > 0)
            {
                var last = list[list.Count - 1];
                if (last.list.Count <= 0)
                {
                    list.Remove(last);
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i].index = i;
            }

            return list;
        }

        static void Shuffle(List<Block> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var k = rand.Next(0, list.Count);
                var tmp = list[k];
                list[k] = list[i];
                list[i] = tmp;
            }
            for (int i = 0; i < list.Count; i++)
            {
                list[i].rndIndex = i;
            }
        }

        static Tuple<MethodDefinition, TypeDefinition> FindMethod(AssemblyDefinition asmDef, string funcName)
        {
            foreach (var module in asmDef.Modules)
            {
                foreach (var type in module.Types)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == funcName)
                        {
                            return new Tuple<MethodDefinition, TypeDefinition>(method, type);
                        }
                    }
                }
            }
            return null;
        }

        static int NextState(List<Block> list, Block block)
        {
            foreach (var i in list)
            {
                if (i.index == block.index + 1)
                {
                    return i.state;
                }
            }
            return -1;
        }

        static void Obfuscate(TypeDefinition type, MethodDefinition method)
        {
            var blockList = SplitIntoBlock(method);
            Shuffle(blockList);

            HashSet<Instruction> brTargetSet = new HashSet<Instruction>();
            foreach (var i in method.Body.Instructions)
            {
                if (i.Operand is Instruction)
                {
                    brTargetSet.Add(i.Operand as Instruction);
                }
            }

            TypeReference retType = method.ReturnType;
            MethodDefinition obsMethod = new MethodDefinition("AddObfuscate", MethodAttributes.Public | MethodAttributes.Static, retType);
            type.Methods.Add(obsMethod);

            foreach (var i in method.Parameters)
            {
                obsMethod.Parameters.Add(i);
            }

            foreach (var i in method.Body.Variables)
            {
                obsMethod.Body.Variables.Add(i);
            }
            VariableDefinition stateVarDef = new VariableDefinition(type.Module.ImportReference(typeof(int)));
            obsMethod.Body.Variables.Add(stateVarDef);

            HashSet<int> stateSet = new HashSet<int>();
            foreach (var i in blockList)
            {
                while (true)
                {
                    int s = rand.Next(200, 500);
                    if (stateSet.Contains(s))
                    {
                        continue;
                    }
                    stateSet.Add(s);
                    i.state = s;
                    break;
                }
            }

            int firstState = 0;
            foreach (var i in blockList)
            {
                if (i.index == 0)
                {
                    firstState = i.state;
                    break;
                }
            }

            var il = obsMethod.Body.GetILProcessor();

            il.Append(il.Create(OpCodes.Ldc_I4, firstState));
            il.Append(il.Create(OpCodes.Stloc, stateVarDef));

            HashSet<Instruction> dispatchSet = new HashSet<Instruction>();
            Dictionary<Instruction, Instruction> dispatch0 = new Dictionary<Instruction, Instruction>();
            Dictionary<Instruction, Instruction> dispatch1 = new Dictionary<Instruction, Instruction>();

            Instruction dispatchStart = il.Create(OpCodes.Nop);
            il.Append(dispatchStart);

            foreach (var i in blockList)
            {
                var i0 = il.Create(OpCodes.Ldc_I4, i.state);
                var i1 = il.Create(OpCodes.Ldloc, stateVarDef);
                var i2 = il.Create(OpCodes.Beq, i.list[0]);
                il.Append(i0);
                il.Append(i1);
                il.Append(i2);

                dispatch0[i2] = i.list[0];
                dispatchSet.Add(i.list[0]);
            }

            Dictionary<Instruction, Instruction> brDict0 = new Dictionary<Instruction, Instruction>();
            Dictionary<Instruction, Instruction> brDict1 = new Dictionary<Instruction, Instruction>();

            foreach (var block in blockList)
            {
                foreach (var ins in block.list)
                {
                    Instruction theIns = null;
                    var operand = ins.Operand;
                    if (operand is Instruction)
                    {
                        theIns = il.Create(ins.OpCode, operand as Instruction);
                        brDict0[theIns] = operand as Instruction;
                    }
                    else if (operand is int)
                    {
                        theIns = il.Create(ins.OpCode, (int)operand);
                    }
                    else
                    {
                        theIns = il.Create(ins.OpCode);
                        theIns.Operand = operand;
                    }

                    il.Append(theIns);

                    if (brTargetSet.Contains(ins))
                    {
                        brDict1[ins] = theIns;
                    }
                    if (dispatchSet.Contains(ins))
                    {
                        dispatch1[ins] = theIns;
                    }
                }

                int nextState = NextState(blockList, block);
                if (nextState != -1)
                {
                    il.Append(il.Create(OpCodes.Ldc_I4, nextState));
                    il.Append(il.Create(OpCodes.Stloc, stateVarDef));
                    il.Append(il.Create(OpCodes.Br, dispatchStart));
                }
            }

            foreach (var i in dispatch0)
            {
                var target = dispatch1[i.Value];
                i.Key.Operand = target;
            }
            foreach (var i in brDict0)
            {
                var target = brDict1[i.Value];
                i.Key.Operand = target;
            }
        }

        static void Main(string[] args)
        {
            string asmPath = @"AddTest.exe";
            var asmDef = AssemblyDefinition.ReadAssembly(asmPath);
            var addFuncPair = FindMethod(asmDef, "Add");
            Obfuscate(addFuncPair.Item2, addFuncPair.Item1);

            asmDef.Write("Patched.exe");

            Console.WriteLine("complete");
            Console.ReadLine();
        }
    }
}
