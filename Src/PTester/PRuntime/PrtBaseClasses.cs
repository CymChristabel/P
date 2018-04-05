﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;


namespace P.Runtime
{
    public abstract class PrtMachine
    {
        #region Fields
        public List<PrtEventValue> sends;
        public string renamedName;
        public bool isSafe;
        public int instanceNumber;
        public List<PrtValue> fields;
        protected PrtValue eventValue;
        protected PrtStateStack stateStack;
        protected PrtFunStack invertedFunStack;
        public PrtContinuation continuation;
        public PrtMachineStatus currentStatus;
        protected PrtNextStatemachineOperation nextSMOperation;
        protected PrtStateExitReason stateExitReason;
        public PrtValue currentTrigger;
        public PrtValue currentPayload;
        public Tuple<string, string> currentTriggerSenderInfo;
        public PrtState destOfGoto;
        //just a reference to stateImpl
        protected StateImpl stateImpl;
        #endregion

        #region Constructor
        public PrtMachine()
        {
            this.instanceNumber = 0;
            this.fields = new List<PrtValue>();
            this.eventValue = PrtValue.@null;
            this.stateStack = new PrtStateStack();
            this.invertedFunStack = new PrtFunStack();
            this.continuation = new PrtContinuation();
            this.currentTrigger = PrtValue.@null;
            this.currentPayload = PrtValue.@null;
            this.currentStatus = PrtMachineStatus.Enabled;
            this.nextSMOperation = PrtNextStatemachineOperation.ExecuteFunctionOperation;
            this.stateExitReason = PrtStateExitReason.NotExit;
            this.sends = new List<PrtEventValue>();
            this.stateImpl = null;
            this.renamedName = null;
            this.isSafe = false;
        }
        #endregion

        public override int GetHashCode()
        {
            return Hashing.Hash(
                renamedName.GetHashCode(),
                isSafe.GetHashCode(),
                instanceNumber.GetHashCode(),
                fields.Select(v => v.GetHashCode()).Hash(),
                eventValue.GetHashCode(),
                stateStack.GetHashCode(),
                invertedFunStack.GetHashCode(),
                continuation.GetHashCode(),
                currentStatus.GetHashCode(),
                nextSMOperation.GetHashCode(),
                stateExitReason.GetHashCode(),
                currentTrigger.GetHashCode(),
                currentPayload.GetHashCode(),
                destOfGoto == null ? Hashing.Hash() : destOfGoto.GetHashCode()
                );
        }

        public abstract string Name
        {
            get;
        }

        public abstract PrtState StartState
        {
            get;
        }

        public abstract void PrtEnqueueEvent(PrtValue e, PrtValue arg, PrtMachine source, PrtMachineValue target = null);

        public PrtState CurrentState
        {
            get
            {
                return stateStack.TopOfStack.state;
            }
        }

        public HashSet<PrtValue> CurrentActionSet
        {
            get
            {
                return stateStack.TopOfStack.actionSet;
            }
        }

        public HashSet<PrtValue> CurrentDefferedSet
        {
            get
            {
                return stateStack.TopOfStack.deferredSet;
            }
        }

        #region Prt Helper functions
        public PrtFun PrtFindActionHandler(PrtValue ev)
        {
            var tempStateStack = stateStack.Clone();
            while (tempStateStack.TopOfStack != null)
            {
                if (tempStateStack.TopOfStack.state.dos.ContainsKey(ev))
                {
                    return tempStateStack.TopOfStack.state.dos[ev];
                }
                else
                    tempStateStack.PopStackFrame();
            }
            Debug.Assert(false);
            return null;
        }

        public void PrtPushState(PrtState s)
        {
            stateStack.PushStackFrame(s);
        }

        public bool PrtPopState(bool isPopStatement)
        {
            Debug.Assert(stateStack.TopOfStack != null);
            //pop stack
            stateStack.PopStackFrame();
            if (stateStack.TopOfStack == null)
            {
                if (isPopStatement)
                {
                    throw new PrtInvalidPopStatement();
                }
                //TODO : Handle the spec machine case separately for the halt event
                else if (!eventValue.Equals(PrtValue.halt))
                {
                    throw new PrtUnhandledEventException(string.Format("{0} failed to handle event {1}", Name, eventValue.ToString()));
                }
                else
                {
                    if (this as PrtImplMachine != null)
                    {
                        stateImpl.TraceLine("<HaltLog> Machine {0}-{1} HALTED with {2} events in the queue", this.Name, this.instanceNumber, (((PrtImplMachine)this).eventQueue).Size());
                    }
                    else
                    {
                        //SpecMachine case:
                        //TODO: is it possible to send "halt" event to a spec machine?
                        stateImpl.TraceLine("<HaltLog> Machine {0}-{1} HALTED", this.Name, this.instanceNumber);
                    }
                    currentStatus = PrtMachineStatus.Halted;
                }
            }

            return currentStatus == PrtMachineStatus.Halted;
        }

        public void PrtChangeState(PrtState s)
        {
            Debug.Assert(stateStack.TopOfStack != null);
            stateStack.PopStackFrame();
            stateStack.PushStackFrame(s);
        }

        public PrtFunStackFrame PrtPopFunStackFrame()
        {
            return invertedFunStack.PopFun();
        }

        public void PrtPushFunStackFrame(PrtFun fun, List<PrtValue> locals)
        {
            if (!fun.IsAnonFun)
            {
                stateImpl.TraceLine("<FunctionLog> Machine {0}-{1} executing Function {2}", this.Name, this.instanceNumber, fun);
            }
            invertedFunStack.PushFun(fun, locals);
        }

        public void PrtPushFunStackFrame(PrtFun fun, List<PrtValue> locals, int retTo)
        {
            invertedFunStack.PushFun(fun, locals, retTo);
        }

        public void PrtPushExitFunction()
        {
            stateImpl.TraceLine("<StateLog> Machine {0}-{1} exiting State {2}", this.Name, this.instanceNumber, CurrentState.name);
            PrtFun exitFun = CurrentState.exitFun;
            if (exitFun.IsAnonFun)
            {
                PrtPushFunStackFrame(exitFun, exitFun.CreateLocals(currentPayload));
            }
            else
            {
                PrtPushFunStackFrame(exitFun, exitFun.CreateLocals());
            }
        }

        public bool PrtIsTransitionPresent(PrtValue ev)
        {
            return CurrentState.transitions.ContainsKey(ev);
        }

        public bool PrtIsActionInstalled(PrtValue ev)
        {
            return CurrentActionSet.Contains(ev);
        }

        public void PrtPushTransitionFun(PrtValue ev)
        {
            PrtFun transitionFun = CurrentState.transitions[ev].transitionFun;
            if (transitionFun.IsAnonFun)
            {
                PrtPushFunStackFrame(transitionFun, transitionFun.CreateLocals(currentPayload));
            }
            else
            {
                PrtPushFunStackFrame(transitionFun, transitionFun.CreateLocals());
            }
        }

        public void PrtFunContReturn(List<PrtValue> retLocals)
        {
            continuation.reason = PrtContinuationReason.Return;
            continuation.retVal = PrtValue.@null;
            continuation.retLocals = retLocals;
        }

        public void PrtFunContReturnVal(PrtValue val, List<PrtValue> retLocals)
        {
            continuation.reason = PrtContinuationReason.Return;
            continuation.retVal = val;
            continuation.retLocals = retLocals;
        }

        public void PrtFunContPop()
        {
            continuation.reason = PrtContinuationReason.Pop;
        }

        public void PrtFunContGoto()
        {
            continuation.reason = PrtContinuationReason.Goto;
        }

        public void PrtFunContRaise()
        {
            continuation.reason = PrtContinuationReason.Raise;
        }

        public void PrtFunContSend(PrtFun fun, List<PrtValue> locals, int ret)
        {
            PrtPushFunStackFrame(fun, locals, ret);
            continuation.reason = PrtContinuationReason.Send;
        }

        public void PrtFunContNewMachine(PrtFun fun, List<PrtValue> locals, int ret)
        {
            PrtPushFunStackFrame(fun, locals, ret);
            continuation.reason = PrtContinuationReason.NewMachine;
        }

        public void PrtFunContReceive(PrtFun fun, List<PrtValue> locals, int ret)
        {
            PrtPushFunStackFrame(fun, locals, ret);
            continuation.reason = PrtContinuationReason.Receive;
        }

        public void PrtFunContNondet(PrtFun fun, List<PrtValue> locals, int ret)
        {
            PrtPushFunStackFrame(fun, locals, ret);
            continuation.reason = PrtContinuationReason.Nondet;
        }
        #endregion
    }

    public class PrtIgnoreFun : PrtFun
    {
        public override bool IsAnonFun
        {
            get
            {
                return true;
            }
        }

        public override void Execute(StateImpl application, PrtMachine parent)
        {
            throw new NotImplementedException();
        }

        public override PrtFunStackFrame CreateFunStackFrame(List<PrtValue> locals, int retLoc)
        {
            throw new NotImplementedException();
        }

        public override List<PrtValue> CreateLocals(params PrtValue[] args)
        {
            throw new NotImplementedException();
        }
    }

    public abstract class PrtFun
    {
        public static PrtIgnoreFun IgnoreFun = new PrtIgnoreFun();

        public abstract bool IsAnonFun
        {
            get;
        } 

        public List<Dictionary<PrtValue, PrtFun>> receiveCases;
        
        public PrtFun()
        {
            receiveCases = new List<Dictionary<PrtValue, PrtFun>>();
        }

        public abstract List<PrtValue> CreateLocals(params PrtValue[] args);

        public abstract PrtFunStackFrame CreateFunStackFrame(List<PrtValue> locals, int retLoc);

        public abstract void Execute(StateImpl application, PrtMachine parent);

        public PrtValue ExecuteToCompletion(StateImpl application, PrtMachine parent, params PrtValue[] args)
        {
            parent.PrtPushFunStackFrame(this, CreateLocals(args));
            Execute(application, parent);
            if (parent.continuation.reason != PrtContinuationReason.Return)
            {
                throw new PrtInternalException("Unexpected continuation reason");
            }
            return parent.continuation.retVal.Clone();
        }
    }

    public class PrtEvent
    {
        public static int DefaultMaxInstances = int.MaxValue;

        public string name;
        public PrtType payloadType;
        public int maxInstances;
        public bool doAssume;

        public PrtEvent(string name, PrtType payload, int mInstances, bool doAssume)
        {
            this.name = name;
            this.payloadType = payload;
            this.maxInstances = mInstances;
            this.doAssume = doAssume;
        }

        public override int GetHashCode()
        {
            return Hashing.Hash(name.GetHashCode(), payloadType.ToString().GetHashCode());
        }
    };

    public class PrtTransition
    {
        public PrtFun transitionFun; // isPush <==> fun == null
        public PrtState gotoState;
        public bool isPushTran;
        public PrtTransition(PrtFun fun, PrtState toState, bool isPush)
        {
            this.transitionFun = fun;
            this.gotoState = toState;
            this.isPushTran = isPush;

        }
    };

    public enum StateTemperature
    {
        Cold,
        Warm,
        Hot
    };

    public class PrtState
    {
        public string name;
        public PrtFun entryFun;
        public PrtFun exitFun;
        public Dictionary<PrtValue, PrtTransition> transitions;
        public Dictionary<PrtValue, PrtFun> dos;
        public HashSet<PrtEventValue> deferredSet;
        public bool hasNullTransition;
        public StateTemperature temperature;

        public PrtState(string name, PrtFun entryFun, PrtFun exitFun, bool hasNullTransition, StateTemperature temperature)
        {
            this.name = name;
            this.entryFun = entryFun;
            this.exitFun = exitFun;
            this.transitions = new Dictionary<PrtValue, PrtTransition>();
            this.dos = new Dictionary<PrtValue, PrtFun>();
            this.deferredSet = new HashSet<PrtEventValue>();
            this.hasNullTransition = hasNullTransition;
            this.temperature = temperature;
        }

        public override int GetHashCode()
        {
            return Hashing.Hash(name.GetHashCode(), temperature.GetHashCode());
        }
    };

    internal class PrtEventNode
    {
        public PrtValue ev;
        public PrtValue arg;
        public string senderMachineName;
        public string senderMachineStateName;

        public PrtEventNode(PrtValue e, PrtValue payload, string senderMachineName, string senderMachineStateName)
        {
            ev = e;
            arg = payload.Clone();
            this.senderMachineName = senderMachineName;
            this.senderMachineStateName = senderMachineStateName;
        }

        public PrtEventNode Clone()
        {
            return new PrtEventNode(this.ev, this.arg, this.senderMachineName, this.senderMachineStateName);
        }

        public override int GetHashCode()
        {
            return Hashing.Hash(ev.GetHashCode(), arg.GetHashCode());
        }
    }

    public class PrtEventBuffer
    {
        List<PrtEventNode> events;
        public PrtEventBuffer()
        {
            events = new List<PrtEventNode>();
        }

        public PrtEventBuffer Clone()
        {
            var clonedVal = new PrtEventBuffer();
            foreach(var ev in this.events)
            {
                clonedVal.events.Add(ev.Clone());
            }
            return clonedVal;
        }
        public int Size()
        {
            return events.Count();
        }
        public int CalculateInstances(PrtValue e)
        {
            return events.Select(en => en.ev).Where(ev => ev == e).Count();
        }

        public void EnqueueEvent(PrtValue e, PrtValue arg, string senderMachineName, string senderMachineStateName)
        {
            Debug.Assert(e is PrtEventValue, "Illegal enqueue of null event");
            PrtEventValue ev = e as PrtEventValue;
            if (ev.evt.maxInstances == PrtEvent.DefaultMaxInstances)
            {
                events.Add(new PrtEventNode(e, arg, senderMachineName, senderMachineStateName));
            }
            else
            {
                if (CalculateInstances(e) == ev.evt.maxInstances)
                {
                    if (ev.evt.doAssume)
                    {
                        throw new PrtAssumeFailureException();
                    }
                    else
                    {
                        throw new PrtMaxEventInstancesExceededException(
                            String.Format(@"< Exception > Attempting to enqueue event {0} more than max instance of {1}\n", ev.evt.name, ev.evt.maxInstances));
                    }
                }
                else
                {
                    events.Add(new PrtEventNode(e, arg, senderMachineName, senderMachineStateName));
                }
            }
        }

        public bool DequeueEvent(PrtImplMachine owner)
        {
            HashSet<PrtValue> deferredSet;
            HashSet<PrtValue> receiveSet;

            deferredSet = owner.CurrentDefferedSet;
            receiveSet = owner.receiveSet;

            int iter = 0;
            while (iter < events.Count)
            { 
                if ((receiveSet.Count == 0 && !deferredSet.Contains(events[iter].ev))
                    || (receiveSet.Count > 0 && receiveSet.Contains(events[iter].ev)))
                {
                    owner.currentTrigger = events[iter].ev;
                    owner.currentPayload = events[iter].arg;
                    owner.currentTriggerSenderInfo = Tuple.Create(events[iter].senderMachineName, events[iter].senderMachineStateName);
                    events.Remove(events[iter]);
                    return true;
                }
                else
                {
                    iter++;
                }
            }

            return false;
        }

        public bool IsEnabled(PrtImplMachine owner)
        {
            HashSet<PrtEventValue> deferredSet;
            HashSet<PrtValue> receiveSet;

            deferredSet = owner.CurrentState.deferredSet;
            receiveSet = owner.receiveSet;
            foreach (var evNode in events)
            {
                if ((receiveSet.Count == 0 && !deferredSet.Contains(evNode.ev))
                    || (receiveSet.Count > 0 && receiveSet.Contains(evNode.ev)))
                {
                    return true;
                }

            }
            return false;
        }

        public override int GetHashCode()
        {
            return events.Select(v => v.GetHashCode()).Hash();
        }
    }

    public class PrtStateStackFrame
    {
        public PrtState state;
        //event value because you cannot defer null value
        public HashSet<PrtValue> deferredSet;
        //action set can have null value
        public HashSet<PrtValue> actionSet;

        public PrtStateStackFrame(PrtState st, HashSet<PrtValue> defSet, HashSet<PrtValue> actSet)
        {
            this.state = st;
            this.deferredSet = new HashSet<PrtValue>();
            foreach (var item in defSet)
                this.deferredSet.Add(item);

            this.actionSet = new HashSet<PrtValue>();
            foreach (var item in actSet)
                this.actionSet.Add(item);
        }

        public PrtStateStackFrame Clone()
        {
            return new PrtStateStackFrame(state, deferredSet, actionSet);
        }

        public override int GetHashCode()
        {
            return state.GetHashCode();
        }
    }
    
    public class PrtStateStack
    {
        public PrtStateStack()
        {
            stateStack = new Stack<PrtStateStackFrame>();
        }

        private Stack<PrtStateStackFrame> stateStack;

        public PrtStateStackFrame TopOfStack
        {
            get
            {
                if (stateStack.Count > 0)
                    return stateStack.Peek();
                else
                    return null;
            }
        }
       
        public PrtStateStack Clone()
        {
            var clone = new PrtStateStack();
            foreach(var s in stateStack.Reverse())
            {
                clone.stateStack.Push(s.Clone());
            }
            return clone;
        }

        public void PopStackFrame()
        {
            stateStack.Pop();
        }


        public void PushStackFrame(PrtState state)
        {
            var deferredSet = new HashSet<PrtValue>();
            if (TopOfStack != null)
            {
                deferredSet.UnionWith(TopOfStack.deferredSet);
            }
            deferredSet.UnionWith(state.deferredSet);
            deferredSet.ExceptWith(state.dos.Keys);
            deferredSet.ExceptWith(state.transitions.Keys);

            var actionSet = new HashSet<PrtValue>();
            if (TopOfStack != null)
            {
                actionSet.UnionWith(TopOfStack.actionSet);
            }
            actionSet.ExceptWith(state.deferredSet);
            actionSet.UnionWith(state.dos.Keys);
            actionSet.ExceptWith(state.transitions.Keys);

            //push the new state on stack
            stateStack.Push(new PrtStateStackFrame(state, deferredSet, actionSet));
        }

        public bool HasNullTransitionOrAction()
        {
            if (TopOfStack.state.hasNullTransition) return true;
            return TopOfStack.actionSet.Contains(PrtValue.@null);
        }

        public override int GetHashCode()
        {
            return stateStack.Select(v => v.GetHashCode()).Hash();
        }
    }

    public enum PrtContinuationReason : int
    {
        Return,
        Nondet,
        Pop,
        Raise,
        Receive,
        Send,
        NewMachine,
        Goto
    };

    public abstract class PrtFunStackFrame
    {
        public int returnToLocation;
        public List<PrtValue> locals;
        
        public PrtFun fun;
        public PrtFunStackFrame(PrtFun fun,  List<PrtValue> locals)
        {
            this.fun = fun;
            this.locals = locals;
            returnToLocation = 0;
        }

        public PrtFunStackFrame(PrtFun fun, List<PrtValue> locals, int retLocation)
        {
            this.fun = fun;
            this.locals = locals;
            returnToLocation = retLocation;
        }

        public virtual PrtFunStackFrame Clone()
        {
            return fun.CreateFunStackFrame(new List<PrtValue>(locals.Select(v => v.Clone())), returnToLocation);
        }

        public override int GetHashCode()
        {
            return Hashing.Hash(returnToLocation.GetHashCode(), locals.Select(v => v.GetHashCode()).Hash());
        }
    }

    public class PrtFunStack
    {
        private Stack<PrtFunStackFrame> funStack;
        public PrtFunStack()
        {
            funStack = new Stack<PrtFunStackFrame>();
        }

        public PrtFunStack Clone()
        {
            var clonedStack = new PrtFunStack();
            foreach(var frame in funStack)
            {
                clonedStack.funStack.Push(frame.Clone());
            }
            clonedStack.funStack.Reverse();
            return clonedStack;
        }

        public void Clear()
        {
            funStack.Clear();
        }

        public PrtFunStackFrame TopOfStack
        {
            get
            {
                if (funStack.Count == 0)
                    return null;
                else
                    return funStack.Peek();
            }
        }

        public void PushFun(PrtFun fun, List<PrtValue> locals)
        {
            funStack.Push(fun.CreateFunStackFrame(locals, 0));
        }

        public void PushFun(PrtFun fun, List<PrtValue> locals, int retLoc)
        {
            funStack.Push(fun.CreateFunStackFrame(locals, retLoc));
        }

        public PrtFunStackFrame PopFun()
        {
            return funStack.Pop();
        }

        public override int GetHashCode()
        {
            return funStack.Select(v => v.GetHashCode()).Hash();
        }
    }

    public class PrtContinuation
    {
        public PrtContinuationReason reason;
        public PrtValue retVal;
        public List<PrtValue> retLocals;
        // The nondet field is different from the fields above because it is used 
        // by ReentrancyHelper to pass the choice to the nondet choice point.
        // Therefore, nondet should not be reinitialized in this class.
        public bool nondet;

        public PrtContinuation()
        {
            reason = PrtContinuationReason.Return;
            retVal = PrtValue.@null;
            nondet = false;
            retLocals = new List<PrtValue>();
        }

        public PrtContinuation Clone()
        {
            var clonedVal = new PrtContinuation();
            clonedVal.reason = this.reason;
            clonedVal.retVal = this.retVal.Clone();
            foreach(var loc in retLocals)
            {
                clonedVal.retLocals.Add(loc.Clone());
            }

            return clonedVal;
        }

        public bool ReturnAndResetNondet()
        {
            var ret = nondet;
            nondet = false;
            return ret;
        }

        public override int GetHashCode()
        {
            return Hashing.Hash(nondet.GetHashCode(), reason.GetHashCode(), retVal.GetHashCode(), retLocals.Select(v => v.GetHashCode()).Hash());
        }
    }
}