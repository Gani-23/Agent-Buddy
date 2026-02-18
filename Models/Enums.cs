namespace AgentBuddy.Models;

/// <summary>
/// Account status categories based on business rules
/// </summary>
public enum AccountCategory
{
    Default,
    Mature,
    AboutToFreeze,
    Advanced,
    NewlyOpened,
    Extended,
    Pending,
    Deposited
}

/// <summary>
/// Account validation status for list management
/// </summary>
public enum AccountValidationStatus
{
    Valid,
    DueSoon,      // Yellow - Due within 30 days
    Invalid,      // Red - Not found in database
    Duplicate     // Pink - Duplicate in lists
}

/// <summary>
/// Half-year period for summary calculations
/// </summary>
public enum HalfYear
{
    FirstHalf,    // January - June
    SecondHalf    // July - December
}

/// <summary>
/// Payment status for accounts
/// </summary>
public enum PaymentStatus
{
    Pending,
    Deposited,
    CollectionDone,
    CollectionStarted
}
