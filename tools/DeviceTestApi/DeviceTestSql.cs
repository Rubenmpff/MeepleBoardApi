using Microsoft.Data.SqlClient;

// Only explicitly selected DeviceTests settings; never appsettings or normal API secrets.
public static class DeviceTestSql {
    public const string Database = "MeepleBoard_DeviceTests";
    public static string Resolve(bool auditOnly) {
        var file = Environment.GetEnvironmentVariable("MEEPLE_DEVICE_TEST_SQL_FILE");
        if (string.IsNullOrWhiteSpace(file)) {
            if (!OperatingSystem.IsWindows() && !auditOnly)
                throw new InvalidOperationException("External DeviceTests SQL configuration is required on this OS.");
            return "Server=(localdb)\\MeepleBoardDeviceTests;Database=MeepleBoard_DeviceTests;Integrated Security=True;TrustServerCertificate=True";
        }
        SqlConnectionStringBuilder sql;
        try { sql = new(File.ReadAllText(file).Trim()); }
        catch { throw new InvalidOperationException("Cannot read valid DeviceTests SQL configuration; connection details suppressed."); }
        var approved = Environment.GetEnvironmentVariable("MEEPLE_DEVICE_TEST_SQL_SERVER");
        if (string.IsNullOrWhiteSpace(approved) || !string.Equals(sql.DataSource, approved, StringComparison.OrdinalIgnoreCase)
            || sql.InitialCatalog != Database || !string.IsNullOrEmpty(sql.AttachDBFilename)
            || sql.UserInstance || sql.IntegratedSecurity || string.IsNullOrWhiteSpace(sql.UserID)
            || string.IsNullOrEmpty(sql.Password) || sql.PersistSecurityInfo
            || sql.DataSource.Contains("localdb", StringComparison.OrdinalIgnoreCase)
            || sql.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Refusing SQL configuration: require approved test endpoint, exact disposable database and encrypted SQL authentication.");
        // Self-signed certificate accepted only for the explicitly selected local test container.
        if (sql.TrustServerCertificate && sql.DataSource != "127.0.0.1,14339")
            throw new InvalidOperationException("Remote DeviceTests SQL requires certificate validation.");
        return sql.ConnectionString;
    }
    public static string Master(string connection) => new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
    public static async Task Probe(string connection) {
        await using var sql = new SqlConnection(Master(connection));
        await sql.OpenAsync();
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(30))";
        Console.WriteLine("SQL connection from host verified; version " + await cmd.ExecuteScalarAsync());
    }
}
