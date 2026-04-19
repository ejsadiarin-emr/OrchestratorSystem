namespace DeploymentPoC.Contracts.Jobs;

public enum ReasonCodes
{
    None = 0,
    // Precondition failures
    PreconditionFailed = 1000,
    ArtifactIntegrityFailed = 1001,
    ArtifactSignatureInvalid = 1002,
    IncompatibleState = 1003,
    MissingDependency = 1004,
    
    // Installation failures
    InstallationFailed = 2000,
    UpgradeFailed = 2001,
    RollbackFailed = 2002,
    PostInstallVerifyFailed = 2003,
    
    // Runtime/communication failures
    LeaseTimeout = 3000,
    HeartbeatLost = 3001,
    SequenceConflict = 3002,
    InvalidPayload = 3003,
    Unauthorized = 3004,
    
    #region PoC Specific (AC mappings)
    // AC-001 - Job submission
    JobSubmissionFailed = 100,
    
    // AC-002 - Job status tracking
    StatusTrackingFailed = 200,
    
    // AC-003 - Message sequencing
    SequencePayloadConflict = 300,
    
    // AC-004 - Agent execution
    AgentExecutionFailed = 400,
    
    // AC-005 - Authentication
    AuthenticationFailed = 500,
    
    // AC-006 - MSI/EXE adapters
    AdapterExecutionFailed = 600,
    
    // AC-007 - Config persistence
    ConfigPersistenceFailed = 700,
    MigrationPathMissing = 701,
    RestoreTriggered = 702,
    
    // AC-101 - Runtime reliability
    LeaseTTLExceeded = 800,
    StaleThresholdExceeded = 801,
    AutoFailBoundExceeded = 802,
    
    // AC-102 - Security controls
    UnsignedArtifactBlocked = 900,
    UnauthorizedRoleDenial = 901,
    SecretRedactionFailed = 902,
    
    // AC-103 - UI timeline updates
    UiUpdateFailed = 1000,
    
    // AC-104 - CLI operations
    CliOperationFailed = 1100,
    
    // AC-105 - Orchestrator packaging
    PackagingFailed = 1200,
    EmbeddedUiLoadFailed = 1201,
    #endregion
}