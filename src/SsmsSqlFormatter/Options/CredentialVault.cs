using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Stores a single secret string in the current Windows user's Credential Manager
    /// (DPAPI-protected) via the native CredWrite/CredRead APIs, instead of plain text.
    /// Every operation is best-effort: a failure (e.g. Credential Manager unavailable)
    /// is reported back to the caller rather than thrown, so callers can fall back
    /// to the previous plain-text storage instead of losing the secret.
    /// </summary>
    public static class CredentialVault
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteNative(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadNative(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteNative(string target, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        private static extern void CredFreeNative(IntPtr buffer);

        /// <summary>Writes (or overwrites, or deletes when <paramref name="secret"/> is empty) the named credential. Returns false on failure.</summary>
        public static bool TrySave(string target, string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                TryDelete(target);
                return true;
            }

            var bytes = Encoding.Unicode.GetBytes(secret);
            IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredPersistLocalMachine,
                    UserName = "SsmsSqlFormatter"
                };
                return CredWriteNative(ref cred, 0);
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }

        /// <summary>Reads the named credential, or null if none is stored or it can't be read.</summary>
        public static string TryLoad(string target)
        {
            IntPtr credPtr = IntPtr.Zero;
            try
            {
                if (!CredReadNative(target, CredTypeGeneric, 0, out credPtr) || credPtr == IntPtr.Zero)
                    return null;

                var cred = (CREDENTIAL)Marshal.PtrToStructure(credPtr, typeof(CREDENTIAL));
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return null;

                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (credPtr != IntPtr.Zero) CredFreeNative(credPtr);
            }
        }

        public static void TryDelete(string target)
        {
            try { CredDeleteNative(target, CredTypeGeneric, 0); } catch { /* best effort */ }
        }
    }
}
