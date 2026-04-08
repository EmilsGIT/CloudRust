using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Interfaces;

public interface IRustPlusFcm : IRustPlusFcmSocket
{
    event EventHandler<FcmMessage>? OnParing;
    event EventHandler<Notification<EntityEvent?>>? OnEntityParing;
    event EventHandler<Notification<ServerEvent?>>? OnServerPairing;
    event EventHandler<Notification<uint?>>? OnSmartSwitchParing;
    event EventHandler<Notification<uint?>>? OnSmartAlarmParing;
    event EventHandler<Notification<uint?>>? OnStorageMonitorParing;
    event EventHandler<AlarmEvent?>? OnAlarmTriggered;
}