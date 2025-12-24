using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PackageGuardian.Core.Transactions
{
    /// <summary>
    /// Minimal transaction helper for bundled Mini Guardian.
    /// </summary>
    public class GuardianTransaction : IDisposable
    {
        private readonly Dictionary<string, byte[]> _fileBackups = new Dictionary<string, byte[]>();
        private bool _committed;
        public string TransactionId { get; }
        public bool IsActive { get; private set; }

        public GuardianTransaction()
        {
            TransactionId = Guid.NewGuid().ToString("N");
            IsActive = true;
        }

        public void BackupFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;
            if (_fileBackups.ContainsKey(filePath))
                return;

            try
            {
                _fileBackups[filePath] = File.ReadAllBytes(filePath);
                Debug.Log($"[Guardian Transaction] Backed up: {Path.GetFileName(filePath)}");
            }
            catch { }
        }

        public void ExecuteFileOperation(string sourcePath, string destPath, FileOperationType operationType)
        {
            if (!IsActive)
                throw new InvalidOperationException("Transaction is not active");

            // Backup affected files
            if (operationType == FileOperationType.Move || operationType == FileOperationType.Delete)
                BackupFile(sourcePath);
            if ((operationType == FileOperationType.Move || operationType == FileOperationType.Copy) && !string.IsNullOrEmpty(destPath))
                BackupFile(destPath);

            // Execute
            switch (operationType)
            {
                case FileOperationType.Copy:
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? "");
                    File.Copy(sourcePath, destPath, true);
                    break;
                case FileOperationType.Move:
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? "");
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    File.Move(sourcePath, destPath);
                    break;
                case FileOperationType.Delete:
                    if (File.Exists(sourcePath))
                        File.Delete(sourcePath);
                    break;
            }
        }

        public void Commit()
        {
            _committed = true;
            IsActive = false;
            _fileBackups.Clear();
            Debug.Log($"[Guardian Transaction] Committed transaction {TransactionId}");
        }

        public void Rollback()
        {
            if (_committed)
                return;

            foreach (var kvp in _fileBackups)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(kvp.Key) ?? "");
                    File.WriteAllBytes(kvp.Key, kvp.Value);
                }
                catch { }
            }
            IsActive = false;
        }

        public void Dispose()
        {
            if (IsActive && !_committed)
                Rollback();
            _fileBackups.Clear();
        }
    }

    public enum FileOperationType
    {
        Copy,
        Move,
        Delete
    }
}



















