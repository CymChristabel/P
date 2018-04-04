﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace P.Runtime
{

    public abstract class StateImpl : ICloneable
    {

        #region Constructors
        /// <summary>
        /// This function is called when the stateimp is loaded first time.
        /// </summary>
        protected StateImpl()
        {
            implMachines = new List<PrtImplMachine>();
            specMachinesMap = new Dictionary<string, PrtSpecMachine>();
            exception = null;
            currentVisibleTrace = new VisibleTrace();
            errorTrace = new StringBuilder();
        }
        #endregion

        #region Fields

        /// <summary>
        /// Map from the statemachine id to the instance of the statemachine.
        /// </summary>
        private List<PrtImplMachine> implMachines;

        public List<PrtImplMachine> ImplMachines
        {
            get
            {
                return implMachines;
            }
        }

        public List<PrtImplMachine> EnabledMachines
        {
            get
            {
                return implMachines.Where(m => m.currentStatus == PrtMachineStatus.Enabled).ToList();
            }
        }
        /// <summary>
        /// List of spec machines
        /// </summary>
        private Dictionary<string, PrtSpecMachine> specMachinesMap;

        public override int GetHashCode()
        {
            var hash1 = implMachines.Select(impl => impl.GetHashCode()).Hash();
            var hash2 = specMachinesMap.Select(tup => tup.Value.GetHashCode()).Hash();
            return Hashing.Hash(hash1, hash2);
        }

        /// <summary>
        /// Stores the exception encoutered during exploration.
        /// </summary>
        private Exception exception;
        
        public VisibleTrace currentVisibleTrace;
        public StringBuilder errorTrace;
        public static List<string> visibleEvents = new List<string>();
        public static List<string> visibleInterfaces = new List<string>();
        public delegate PrtImplMachine CreateMachineDelegate(StateImpl application, PrtValue payload);
        public delegate PrtSpecMachine CreateSpecDelegate(StateImpl application);
        public static Dictionary<string, Dictionary<string, string>> linkMap = new Dictionary<string, Dictionary<string, string>>();
        public static Dictionary<string, string> machineDefMap = new Dictionary<string, string>();
        public static Dictionary<string, bool> isSafeMap = new Dictionary<string, bool>();
        public static Dictionary<string, List<string>> specMachineMap = new Dictionary<string, List<string>>();
        public static Dictionary<string, CreateMachineDelegate> createMachineMap = new Dictionary<string, CreateMachineDelegate>();
        public static Dictionary<string, CreateSpecDelegate> createSpecMap = new Dictionary<string, CreateSpecDelegate>();
        public static Dictionary<string, List<PrtEventValue>> interfaceMap = new Dictionary<string, List<PrtEventValue>>();

        public delegate bool GetBooleanChoiceDelegate();
        public GetBooleanChoiceDelegate UserBooleanChoice = null;

        public delegate void CreateMachineCallbackDelegate(PrtImplMachine machine);
        public CreateMachineCallbackDelegate CreateMachineCallback = null;

        public delegate void DequeueCallbackDelegate(PrtImplMachine machine, string evName, string senderMachineName, string senderMachineStateName);
        public DequeueCallbackDelegate DequeueCallback = null;

        public delegate void StateTransitionCallbackDelegate(PrtImplMachine machine, PrtState from, PrtState to, string reason);
        public StateTransitionCallbackDelegate StateTransitionCallback = null;

        #endregion

        #region Getters and Setters
        public bool Deadlock
        {
            get
            {
                bool enabled = false;
                foreach (var x in implMachines)
                {
                    if (enabled) break;
                    enabled = enabled || (x.currentStatus == PrtMachineStatus.Enabled);
                }
                bool hot = false;
                foreach (var x in specMachinesMap.Values)
                {
                    if (hot) break;
                    hot = hot || x.currentTemperature == StateTemperature.Hot;
                }
                return (!enabled && hot);
            }
        }

        public Exception Exception
        {
            get { return exception; }
            set { exception = value; }
        }

        
        #endregion

        #region Clone Function
        public abstract StateImpl MakeSkeleton();

        public object Clone()
        {
            var clonedState = MakeSkeleton();
            //clone all the fields
            clonedState.implMachines = new List<PrtImplMachine>();
            foreach (var machine in implMachines)
            {
                clonedState.implMachines.Add(machine.Clone(clonedState));
            }

            clonedState.specMachinesMap = new Dictionary<string, PrtSpecMachine>();
            foreach (var specMachine in specMachinesMap)
            {
                clonedState.specMachinesMap.Add(specMachine.Key, (specMachine.Value).Clone(clonedState));
            }

            clonedState.exception = this.exception;

            clonedState.currentVisibleTrace = new VisibleTrace();
            foreach(var item in currentVisibleTrace.Trace)
            {
                clonedState.currentVisibleTrace.Trace.Add(item);
            }

            clonedState.errorTrace = new StringBuilder(errorTrace.ToString());
            return clonedState;

        }
        #endregion

        public List<PrtSpecMachine> GetAllSpecMachines()
        {
            return specMachinesMap.Values.ToList();
        }

        private List<PrtSpecMachine> GetSpecMachines(string currMachine)
        {
            var allSpecMachines = specMachineMap.Where(mon => mon.Value.Contains(currMachine))
                                        .Select(item => item.Key)
                                        .Select(monName => specMachinesMap[monName]).ToList();
            return allSpecMachines;
        }
        public PrtInterfaceValue CreateInterface(PrtMachine currMach, string interfaceOrMachineName, PrtValue payload)
        {
            //add visible action to trace
            if(visibleInterfaces.Contains(interfaceOrMachineName))
            {
                currentVisibleTrace.AddAction(interfaceOrMachineName, payload.ToString());
            }

            var renamedImpMachine = linkMap[currMach.renamedName][interfaceOrMachineName];
            var impMachineName = machineDefMap[renamedImpMachine];
            var machine = createMachineMap[impMachineName](this, payload);
            machine.isSafe = isSafeMap[renamedImpMachine];
            machine.renamedName = renamedImpMachine;
            AddImplMachineToStateImpl(machine);

            CreateMachineCallback?.Invoke(machine);

            //TraceLine("<CreateLog> Machine {0}-{1} was created by machine {2}-{3}", currMach.renamedName, currMach.instanceNumber, machine.renamedName, machine.instanceNumber);
            TraceLine("<CreateLog> Machine {0}-{1} was created by machine {2}-{3}", machine.renamedName, machine.instanceNumber, currMach.renamedName, currMach.instanceNumber);

            if (interfaceMap.ContainsKey(interfaceOrMachineName))
            {
                return new PrtInterfaceValue(machine, interfaceMap[interfaceOrMachineName]);
            }
            else
            {
                return new PrtInterfaceValue(machine, machine.self.permissions);
            }
        }

        public void CreateMainMachine(string mainInterface)
        {
            

            if (!machineDefMap.ContainsKey(mainInterface))
            {
                throw new PrtInternalException($"No {mainInterface} interface in the module machine definition map");
            }
            var impMachineName = machineDefMap[mainInterface];
            var machine = createMachineMap[impMachineName](this, PrtValue.@null);
            machine.isSafe = isSafeMap[mainInterface];
            machine.renamedName = mainInterface;
            AddImplMachineToStateImpl(machine);
            TraceLine("<CreateLog> Main machine {0} was created by machine Runtime", impMachineName);

        }

        public void CreateSpecMachine(string renamedSpecName)
        {
            TraceLine("<CreateLog> Spec Machine {0} was created by machine Runtime", renamedSpecName);
            var impSpecMachine = machineDefMap[renamedSpecName];
            var machine = createSpecMap[impSpecMachine](this);
            machine.isSafe = isSafeMap[renamedSpecName];
            machine.renamedName = renamedSpecName;
            AddSpecMachineToStateImpl(machine);
        }
        public int NextMachineInstanceNumber(Type machineType)
        {
            return implMachines.Where(m => m.GetType() == machineType).Count() + 1;
        }

        public void Announce(PrtEventValue ev, PrtValue payload, PrtMachine parent)
        {
            if (ev.Equals(PrtValue.@null))
            {
                throw new PrtIllegalEnqueueException("Enqueued event must not be null");
            }

            PrtType prtType = ev.evt.payloadType;
            //assertion to check if argument passed inhabits the payload type.
            if (prtType is PrtNullType)
            {
                if (!payload.Equals(PrtValue.@null))
                {
                    throw new PrtIllegalEnqueueException("Did not expect a payload value");
                }
            }
            else if (!PrtValue.PrtInhabitsType(payload, prtType))
            {
                throw new PrtInhabitsTypeException(String.Format("Payload <{0}> does not match the expected type <{1}> with event <{2}>", payload.ToString(), prtType.ToString(), ev.evt.name));
            }

            var allSpecMachines = GetSpecMachines(parent.renamedName);
            foreach (var mon in allSpecMachines)
            {
                if (mon.observes.Contains(ev))
                {
                    TraceLine("<AnnounceLog> Enqueued Event <{0}, {1}> to Spec Machine {2}", ev, payload, mon.Name);
                    mon.PrtEnqueueEvent(ev, payload, parent);
                }
            }
        }

        public void AddImplMachineToStateImpl(PrtImplMachine machine)
        {
            implMachines.Add(machine);
        }

        public void AddSpecMachineToStateImpl(PrtSpecMachine spec)
        {
            specMachinesMap.Add(spec.renamedName, spec);
        }

        public void Trace(string message, params object[] arguments)
        {
            message = message.Replace(@"\n", System.Environment.NewLine);
            errorTrace.Append(String.Format(message, arguments));
        }

        public void TraceLine(string message, params object[] arguments)
        {
            message = message.Replace(@"\n", System.Environment.NewLine);
            errorTrace.AppendLine(String.Format(message, arguments));
        }



        public void SetPendingChoicesAsBoolean(PrtImplMachine process)
        {
            //TODO: NOT IMPLEMENT YET
            //throw new NotImplementedException();
        }

        public Boolean GetSelectedChoiceValue(PrtImplMachine process)
        {
            if (UserBooleanChoice != null)
            {
                return UserBooleanChoice();
            }
            else
            {
                //throw new NotImplementedException();
                return (new Random(DateTime.Now.Millisecond)).Next(10) > 5;
            }
        }
    }

    
}
