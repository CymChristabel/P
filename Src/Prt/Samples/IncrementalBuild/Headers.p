//Events
event eD0Entry assume 1;
event eD0Exit assume 1;
event eTimerFired assert 1;
event eSwitchStatusChange assume 1;
event eTransferSuccess assume 1;
event eTransferFailure assume 1;
event eStopTimer assume 1;
event eUpdateBarGraphStateUsingControlTransfer assume 1;
event eSetLedStateToUnstableUsingControlTransfer assume 1;
event eStartDebounceTimer assume 1;
event eSetLedStateToStableUsingControlTransfer assume 1;
event eStoppingSuccess assert 1;
event eStoppingFailure assert 1;
event eOperationSuccess assert 1;
event eOperationFailure assert 1;
event eTimerStopped assert 1;
event eYes assert 1;
event eNo assert 1;
event eUnit assert 1;



type OSRDriverInterface() = {
  eSwitchStatusChange, eD0Exit, eD0Entry, eOperationSuccess, eTransferSuccess,
  eTransferFailure, eTimerFired, eTimerStopped,eStoppingSuccess, eStoppingFailure
};

type SwitchInterface(OSRDriverInterface) = { eYes };

type TimerInterface(OSRDriverInterface) = {
  eStartDebounceTimer, eStopTimer
};
type LEDInterface(OSRDriverInterface) = {
  eUpdateBarGraphStateUsingControlTransfer, eSetLedStateToUnstableUsingControlTransfer,
  eSetLedStateToStableUsingControlTransfer
};



