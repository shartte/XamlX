using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using SreOpCode = System.Reflection.Emit.OpCode;
using SreOpCodes = System.Reflection.Emit.OpCodes;

namespace XamlX.TypeSystem
{
    partial class CecilTypeSystem
    {
        public class CecilEmitter : IXamlXEmitter
        {
            TypeReference Import(TypeReference r)
            {
                var rv = M.ImportReference(r);
                if (r is GenericInstanceType gi)
                {
                    for (var c = 0; c < gi.GenericArguments.Count; c++)
                        gi.GenericArguments[c] = Import(gi.GenericArguments[c]);
                }

                return rv;
            }

            FieldReference Import(FieldReference f)
            {
                var rv = M.ImportReference(f);
                rv.FieldType = Import(rv.FieldType);
                return rv;
            }
            
            private static Dictionary<SreOpCode, OpCode> Dic = new Dictionary<SreOpCode, OpCode>();
            private List<CecilLabel> _markedLabels = new List<CecilLabel>();
            static CecilEmitter()
            {

                foreach (var sreField in typeof(SreOpCodes)
                    .GetFields(BindingFlags.Static | BindingFlags.Public)
                    .Where(f=>f.FieldType == typeof(SreOpCode)))

                {
                    var sre = (SreOpCode) sreField.GetValue(null);
                    var cecilField = typeof(OpCodes).GetField(sreField.Name);
                    if(cecilField == null)
                        continue;
                    var cecil = (OpCode)cecilField.GetValue(null);
                    Dic[sre] = cecil;
                }       
            }
            
            
            private readonly MethodBody _body;
            private readonly MethodDefinition _method;
            private CecilDebugPoint _lastDebugPoint;
            public CecilEmitter(IXamlXTypeSystem typeSystem, MethodDefinition method)
            {
                _method = method;
                _body = method.Body;
                TypeSystem = typeSystem;
            }


            public IXamlXTypeSystem TypeSystem { get; }

            IXamlXEmitter Emit(Instruction i)
            {
                _body.Instructions.Add(i);
                foreach (var ml in _markedLabels)
                {
                    foreach(var instruction in _body.Instructions)
                        if (instruction.Operand == ml.Instruction)
                            instruction.Operand = i;
                    ml.Instruction = i;
                }
                _markedLabels.Clear();
                return this;
            }

            ParameterDefinition GetParameter(int arg)
            {
                if (_body.Method.HasThis)
                {
                    if (arg == 0)
                        return _body.ThisParameter;
                    arg--;
                }

                return _body.Method.Parameters[arg];
            }
            
            private Instruction CreateI(OpCode code, int arg)
            {
                if (code.OperandType == OperandType.ShortInlineArg || code.OperandType == OperandType.InlineArg)
                    return Instruction.Create(code, GetParameter(arg));
                if (code.OperandType == OperandType.InlineVar || code.OperandType == OperandType.ShortInlineVar)
                    return Instruction.Create(code, _body.Variables[arg]);
                return Instruction.Create(code, arg);
            }

            private ModuleDefinition M => _body.Method.Module;

            public IXamlXEmitter Emit(SreOpCode code)
                => Emit(Instruction.Create(Dic[code]));

            public IXamlXEmitter Emit(SreOpCode code, IXamlXField field)
            {
                return Emit(Instruction.Create(Dic[code], Import(((CecilField) field).Field)));
            }

            public IXamlXEmitter Emit(SreOpCode code, IXamlXMethod method)
                => Emit(Instruction.Create(Dic[code], M.ImportReference(((CecilMethod) method).IlReference)));

            public IXamlXEmitter Emit(SreOpCode code, IXamlXConstructor ctor)
                => Emit(Instruction.Create(Dic[code], M.ImportReference(((CecilConstructor) ctor).IlReference)));

            public IXamlXEmitter Emit(SreOpCode code, string arg)
                => Emit(Instruction.Create(Dic[code], arg));

            public IXamlXEmitter Emit(SreOpCode code, int arg)
                => Emit(CreateI(Dic[code], arg));
            
            public IXamlXEmitter Emit(SreOpCode code, long arg)
                => Emit(Instruction.Create(Dic[code], arg));
            
            public IXamlXEmitter Emit(SreOpCode code, IXamlXType type)
                => Emit(Instruction.Create(Dic[code], Import(((ITypeReference) type).Reference)));

            public IXamlXEmitter Emit(SreOpCode code, float arg)
                => Emit(Instruction.Create(Dic[code], arg));

            public IXamlXEmitter Emit(SreOpCode code, double arg)
                => Emit(Instruction.Create(Dic[code], arg));


            class CecilLocal : IXamlXLocal
            {
                public VariableDefinition Variable { get; set; }
            }

            class CecilLabel : IXamlXLabel
            {
                public Instruction Instruction { get; set; } = Instruction.Create(OpCodes.Nop);
            }

            public IXamlXLocal DefineLocal(IXamlXType type)
            {
                var r = Import(((ITypeReference) type).Reference);
                var def = new VariableDefinition(r);
                _body.Variables.Add(def);
                return new CecilLocal {Variable = def};
            }

            public IXamlXLabel DefineLabel() => new CecilLabel();

            public IXamlXEmitter MarkLabel(IXamlXLabel label)
            {
                _markedLabels.Add((CecilLabel) label);
                return this;
            }

            public IXamlXEmitter Emit(SreOpCode code, IXamlXLabel label)
                => Emit(Instruction.Create(Dic[code], ((CecilLabel) label).Instruction));

            public IXamlXEmitter Emit(SreOpCode code, IXamlXLocal local)
                => Emit(Instruction.Create(Dic[code], ((CecilLocal) local).Variable));

            private static readonly Guid LanguageGuid = new Guid("9a37fc74-96b5-4dbc-8b8a-c4e603735a63");
            private static readonly Guid LanguageVendorGuid = new Guid("3c631bf9-0cbe-4aab-a24a-5e417734441c");
            class CecilDebugPoint : IDisposable
            {
                private readonly CecilEmitter _parent;
                private readonly Document _doc;
                private readonly int _line;
                private readonly int _position;
                public int StartOffset { get; }

                public CecilDebugPoint(CecilEmitter parent, Document doc, int line, int position, int startOffset)
                {
                    _parent = parent;
                    _doc = doc;
                    _line = line;
                    _position = position;
                    StartOffset = startOffset;
                }

                public void Dispose()
                {
                    if (_parent._body.Instructions.Count <= StartOffset)
                        return;
                    var instruction = _parent._body.Instructions[StartOffset];
                    var dbg = _parent._method.DebugInformation;
                    if (dbg.Scope == null)
                    {
                        dbg.Scope = new ScopeDebugInformation(instruction, instruction)
                        {
                            End = new InstructionOffset(),
                            Import = new ImportDebugInformation()
                        };
                    }

                    dbg.SequencePoints.Add(new SequencePoint(instruction, _doc)
                    {
                        StartLine = _line,
                        StartColumn = _position,
                        EndLine = _line,
                        EndColumn = _position + 1
                    });
                }
            }

            class EmptyDisposable : IDisposable
            {
                public void Dispose()
                {
                    
                }
            }
            
            static ConditionalWeakTable<AssemblyDefinition, Dictionary<string, Document>>
                _documents = new ConditionalWeakTable<AssemblyDefinition, Dictionary<string, Document>>();
            
            public IDisposable BeginDebugBlock(IFileSource file, int line, int position)
            {
                if (!_documents.TryGetValue(_method.Module.Assembly, out var documents))
                    _documents.Add(_method.Module.Assembly, documents = new Dictionary<string, Document>());
                
                
                if (!documents.TryGetValue(file.FilePath, out var doc))
                {
                    byte[] hash;
                    using (var sha1 = SHA1.Create())
                        hash = sha1.ComputeHash(file.FileContents);
                    
                    documents[file.FilePath] = doc = new Document(file.FilePath)
                    {
                        LanguageGuid = LanguageGuid,
                        LanguageVendorGuid = LanguageVendorGuid,
                        Type = DocumentType.Text,
                        HashAlgorithm = DocumentHashAlgorithm.SHA1,
                        Hash = hash,
                    };
                }

                if (_lastDebugPoint != null && _lastDebugPoint.StartOffset == _body.Instructions.Count)
                    return new EmptyDisposable();
                return _lastDebugPoint = new CecilDebugPoint(this, doc, line, position, _body.Instructions.Count);
            }
        }
    }
}
