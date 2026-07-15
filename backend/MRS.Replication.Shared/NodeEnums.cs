namespace MRS.Replication.Shared;

public enum NodeRole
{
    Primary,
    Replica
}

public enum NodeStatus
{
    Provisioning,
    Active,
    Delayed,
    Resyncing,
    Inactive,
    Failed
}

public enum ReplicationMode
{
    Sync,
    Async
}

public enum NodeEventType
{
    Registered,
    StatusChanged,
    FailoverStarted,
    FailoverCompleted,
    ResyncStarted,
    ResyncCompleted,
    Removed,
    Alert
}
