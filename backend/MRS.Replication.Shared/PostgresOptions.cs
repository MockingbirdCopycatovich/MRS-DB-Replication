namespace MRS.Replication.Shared;

/// <summary>Credentials shared by every MRS-provisioned Postgres node — set once via the Setup Wizard, used by Watchdog and the Backend alike.</summary>
public sealed class PostgresOptions
{
    public string User { get; set; } = "mrs_user";
    public string Password { get; set; } = "mrs_password";
    public string Database { get; set; } = "mrs_db";

    public string BuildConnectionString(string host, int port, int timeoutSeconds = 3) =>
        $"Host={host};Port={port};Username={User};Password={Password};Database={Database};" +
        $"Timeout={timeoutSeconds};Command Timeout={timeoutSeconds};Pooling=false";
}
