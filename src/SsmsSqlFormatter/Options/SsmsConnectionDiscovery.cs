using System;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.Reflection;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Reads the active query window's connection so "Expand SELECT *" can resolve real
    /// table/view structure with zero extra setup. Reaches into SSMS's own
    /// Microsoft.SqlServer.Management.UI.VSIntegration API entirely through reflection -
    /// there is no compile-time reference to that assembly anywhere in this project. It
    /// only ships inside an SSMS install (never as a NuGet package), so referencing it at
    /// compile time would break CI (which builds on a bare runner with no SSMS installed)
    /// and checking the DLL into the repo would raise redistribution concerns. At runtime,
    /// though, we're actually running inside SSMS's own process, so the assembly is already
    /// loaded in this AppDomain - we just have to find it by name instead of linking it.
    ///
    /// Every step here is wrapped defensively: any missing type, missing property, or
    /// unexpected shape (across SSMS versions, or when not running inside SSMS at all, e.g.
    /// unit tests) simply results in a null return - "no connection available" is a normal,
    /// expected outcome that the caller (SelectStarExpander) already treats as "leave
    /// SELECT * untouched," never an error. This is also the one piece of the whole feature
    /// that cannot be verified by an automated test or CI - it can only be exercised by
    /// actually running inside SSMS with a live connection, and the exact property shapes
    /// used below (particularly around SQL Server Authentication credentials, which SSMS
    /// may keep encrypted rather than exposing a plaintext password at all) should be
    /// expected to need adjustment after that first real test. Windows/Integrated
    /// Authentication connections are the reliable case; SQL Server Authentication
    /// connections may simply come back unresolved.
    /// </summary>
    public static class SsmsConnectionDiscovery
    {
        /// <summary>Best-effort connection string for the active query window, or null if it can't be determined.</summary>
        public static string TryGetActiveConnectionString()
        {
            try
            {
                var activeConnectionInfo = GetCurrentlyActiveWndConnectionInfo();
                if (activeConnectionInfo == null) return null;

                var uiConnectionInfo = GetInstanceProperty(activeConnectionInfo, "UIConnectionInfo");
                if (uiConnectionInfo == null) return null;

                string server = GetInstanceProperty(uiConnectionInfo, "ServerName") as string;
                if (string.IsNullOrEmpty(server)) return null;

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = server,
                    ConnectTimeout = 5
                };

                string database =
                    GetInstanceProperty(activeConnectionInfo, "DatabaseName") as string ??
                    GetAdvancedOption(uiConnectionInfo, "DATABASE") ??
                    GetInstanceProperty(uiConnectionInfo, "InitialDatabase") as string;
                if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;

                if (IsIntegratedSecurity(uiConnectionInfo))
                {
                    builder.IntegratedSecurity = true;
                }
                else
                {
                    string userName = GetInstanceProperty(uiConnectionInfo, "UserName") as string;
                    string password = GetInstanceProperty(uiConnectionInfo, "Password") as string;
                    if (string.IsNullOrEmpty(userName) || password == null) return null;
                    builder.UserID = userName;
                    builder.Password = password;
                }

                return builder.ConnectionString;
            }
            catch
            {
                return null;
            }
        }

        private static object GetCurrentlyActiveWndConnectionInfo()
        {
            var serviceCacheType = FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
            if (serviceCacheType == null) return null;

            var scriptFactory = GetStaticProperty(serviceCacheType, "ScriptFactory");
            if (scriptFactory == null) return null;

            return GetInstanceProperty(scriptFactory, "CurrentlyActiveWndConnectionInfo");
        }

        private static bool IsIntegratedSecurity(object uiConnectionInfo)
        {
            var authType = GetInstanceProperty(uiConnectionInfo, "AuthenticationType");
            if (authType is int authTypeInt) return authTypeInt == 0; // 0 = Windows Authentication, 1 = SQL Server Authentication

            var flag = GetInstanceProperty(uiConnectionInfo, "UseIntegratedSecurity");
            if (flag is bool flagBool) return flagBool;

            // Can't tell - default to integrated security rather than ever inventing a
            // fake SQL-auth credential.
            return true;
        }

        private static string GetAdvancedOption(object uiConnectionInfo, string key)
        {
            var advanced = GetInstanceProperty(uiConnectionInfo, "AdvancedOptions") as NameValueCollection;
            return advanced?[key];
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(fullName, throwOnError: false); }
                catch { continue; }
                if (type != null) return type;
            }
            return null;
        }

        private static object GetStaticProperty(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            return prop?.GetValue(null);
        }

        private static object GetInstanceProperty(object instance, string propertyName)
        {
            if (instance == null) return null;
            var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(instance);
        }
    }
}
