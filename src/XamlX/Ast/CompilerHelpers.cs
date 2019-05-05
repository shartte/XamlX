using System.Reflection.Emit;
using XamlX.Transform;
using XamlX.Transform.Emitters;
using XamlX.TypeSystem;
using Visitor = XamlX.Ast.IXamlXAstVisitor;
namespace XamlX.Ast
{
    public class XamlXAstCompilerLocalNode : XamlXAstNode, IXamlXAstValueNode, IXamlXAstEmitableNode
    {
        private XamlXAstClrTypeReference _typeReference;
        public IXamlXType Type { get; }
        public XamlXAstCompilerLocalNode(IXamlXLineInfo lineInfo, XamlXAstClrTypeReference type) : base(lineInfo)
        {
            Type = type.Type;
            _typeReference = type;
        }

        public XamlXAstCompilerLocalNode(IXamlXAstValueNode value) : this(value, value.Type.GetClrTypeReference())
        {
            
        }
        
        IXamlXAstTypeReference IXamlXAstValueNode.Type => _typeReference;
        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            context.LdLocal(this, codeGen);
            return XamlXNodeEmitResult.Type(0, Type);
        }
    }

    public class XamlXAstLocalInitializationNodeEmitter : XamlXValueWithSideEffectNodeBase, IXamlXAstEmitableNode
    {
        public XamlXAstCompilerLocalNode Local { get; set; }

        public XamlXAstLocalInitializationNodeEmitter(IXamlXLineInfo lineInfo,
            IXamlXAstValueNode value,
            XamlXAstCompilerLocalNode local) : base(lineInfo, value)
        {
            Value = value;
            Local = local;
        }

        public override void VisitChildren(Visitor visitor)
        {
            base.VisitChildren(visitor);
            Local = (XamlXAstCompilerLocalNode) Local.Visit(visitor);
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            var rv = context.Emit(Value, codeGen, Local.Type);
            codeGen.Emit(OpCodes.Dup);
            context.StLocal(Local, codeGen);
            return XamlXNodeEmitResult.Type(0, rv.ReturnType);
        }
    }

    public class XamlXValueNodeWithBeginInit : XamlXValueWithSideEffectNodeBase, IXamlXAstEmitableNode
    {
        public XamlXValueNodeWithBeginInit(IXamlXAstValueNode value) : base(value, value)
        {
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            var res = context.Emit(Value, codeGen, Value.Type.GetClrType());
            var supportInitType = context.Configuration.TypeMappings.SupportInitialize;
            var supportsInitialize = supportInitType != null
                                     && context.Configuration.TypeMappings.SupportInitialize
                                         .IsAssignableFrom(Value.Type.GetClrType());
            if (supportsInitialize)
            {
                codeGen
                    .Dup()
                    .EmitCall(supportInitType.FindMethod(m => m.Name == "BeginInit"));
            }

            return res;
        }
    }

    public class XamlXAstManipulationImperativeNode : XamlXAstNode, IXamlXAstManipulationNode, IXamlXAstEmitableNode
    {
        public IXamlXAstImperativeNode Imperative { get; set; }

        public XamlXAstManipulationImperativeNode(IXamlXLineInfo lineInfo, IXamlXAstImperativeNode imperative) 
            : base(lineInfo)
        {
            Imperative = imperative;
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            // Discard the stack value we are "supposed" to manipulate
            codeGen.Emit(OpCodes.Pop);
            context.Emit(Imperative, codeGen, null);
            return XamlXNodeEmitResult.Void(1);
        }

        public override void VisitChildren(Visitor visitor)
        {
            Imperative = (IXamlXAstImperativeNode)Imperative.Visit(visitor);
        }
    }

    public class XamlXAstImperativeValueManipulation : XamlXAstNode, IXamlXAstImperativeNode, IXamlXAstEmitableNode
    {
        public IXamlXAstValueNode Value { get; set; }
        public IXamlXAstManipulationNode Manipulation { get; set; }

        public XamlXAstImperativeValueManipulation(IXamlXLineInfo lineInfo, 
            IXamlXAstValueNode value, IXamlXAstManipulationNode manipulation) : base(lineInfo)
        {
            Value = value;
            Manipulation = manipulation;
        }

        public override void VisitChildren(Visitor visitor)
        {
            Value = (XamlXAstCompilerLocalNode) Value.Visit(visitor);
            Manipulation = (IXamlXAstManipulationNode) Manipulation.Visit(visitor);
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            context.Emit(Value, codeGen, Value.Type.GetClrType());
            context.Emit(Manipulation, codeGen, null);
            return XamlXNodeEmitResult.Void(0);
        }
    }

    public class XamlXAstContextLocalNode : XamlXAstNode, IXamlXAstValueNode, IXamlXAstEmitableNode
    {
        public XamlXAstContextLocalNode(IXamlXLineInfo lineInfo, IXamlXType type) : base(lineInfo)
        {
            Type = new XamlXAstClrTypeReference(this, type, false);
        }

        public IXamlXAstTypeReference Type { get; }
        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            codeGen.Ldloc(context.ContextLocal);
            return XamlXNodeEmitResult.Type(0, Type.GetClrType());
        }
    }

    public class XamlXAstRuntimeCastNode : XamlXAstNode, IXamlXAstValueNode, IXamlXAstEmitableNode
    {
        public XamlXAstRuntimeCastNode(IXamlXLineInfo lineInfo, IXamlXAstValueNode value, IXamlXAstTypeReference castTo) : base(lineInfo)
        {
            Value = value;
            Type = castTo;
        }
        public IXamlXAstValueNode Value { get; set; }
        public IXamlXAstTypeReference Type { get; set; }

        public override void VisitChildren(Visitor visitor)
        {
            Value = (IXamlXAstValueNode) Value.Visit(visitor);
            Type = (IXamlXAstTypeReference) Type.Visit(visitor);
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            context.Emit(Value, codeGen, context.Configuration.WellKnownTypes.Object);
            var t = Type.GetClrType();
            if (t.IsValueType)
                codeGen.Unbox_Any(t);
            else
                codeGen.Castclass(t);            
            return XamlXNodeEmitResult.Type(0, t);
        }
    }

    public class XamlXAstNeedsParentStackValueNode : XamlXValueWithSideEffectNodeBase,
        IXamlXAstEmitableNode,
        IXamlXAstNodeNeedsParentStack
    {
        public XamlXAstNeedsParentStackValueNode(IXamlXLineInfo lineInfo, IXamlXAstValueNode value) : base(lineInfo, value)
        {
        }

        public XamlXNodeEmitResult Emit(XamlXEmitContext context, IXamlXEmitter codeGen)
        {
            XamlXNeedsParentStackCache.Verify(context, this);
            return context.Emit(Value, codeGen, Value.Type.GetClrType());
        }

        public bool NeedsParentStack => true;
    }

}
