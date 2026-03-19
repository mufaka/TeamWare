namespace TeamWare.Web.Models;

public enum ActivityChangeType
{
    Created = 0,
    StatusChanged = 1,
    PriorityChanged = 2,
    Assigned = 3,
    Unassigned = 4,
    MarkedNextAction = 5,
    ClearedNextAction = 6,
    MarkedSomedayMaybe = 7,
    ClearedSomedayMaybe = 8,
    Updated = 9,
    Deleted = 10
}
