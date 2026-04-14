using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class GeneratedPackageGuardianTemplateHardeningTests
    {
        private string _workspaceRoot;

        [SetUp]
        public void SetUp()
        {
            _workspaceRoot = Path.Combine(
                ProjectRoot,
                "Library",
                "CopilotTests",
                "GeneratedPackageGuardianTemplate",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspaceRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_workspaceRoot))
            {
                Directory.Delete(_workspaceRoot, true);
            }
        }

        [Test]
        public void GuardianTransaction_Rollback_RemovesCreatedDestinationAndRestoresSource()
        {
            string source = CreateTextFile("source.txt", "original-template-content");
            string destination = Path.Combine(_workspaceRoot, "destination.txt");

            object transaction = CreateTransaction();
            InvokeTransactionMethod(
                transaction,
                "ExecuteFileOperation",
                source,
                destination,
                GetFileOperationTypeValue("Move"));

            Assert.That(File.Exists(source), Is.False);
            Assert.That(File.Exists(destination), Is.True);

            InvokeTransactionMethod(transaction, "Rollback");

            Assert.That(File.Exists(source), Is.True);
            Assert.That(File.ReadAllText(source), Is.EqualTo("original-template-content"));
            Assert.That(File.Exists(destination), Is.False, "Rollback should remove files that did not exist before the transaction.");
        }

        [Test]
        public void GuardianTransaction_BackupFile_ThrowsWhenSnapshotCannotBeCaptured()
        {
            string source = CreateTextFile("locked.txt", "locked-template-content");
            object transaction = CreateTransaction();

            using var lockStream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            MethodInfo method = GetTemplateGuardianTransactionType().GetMethod("BackupFile", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            Exception ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(transaction, new object[] { source }));
            Assert.That(ex?.InnerException, Is.TypeOf<IOException>());
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static Type GetTemplateGuardianTransactionType()
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "YUCP.DirectVpmInstaller.Template", StringComparison.Ordinal));

            Assert.That(assembly, Is.Not.Null, "Expected YUCP.DirectVpmInstaller.Template assembly to be loaded.");

            Type type = assembly.GetType("PackageGuardian.Core.Transactions.GuardianTransaction", throwOnError: false);
            Assert.That(type, Is.Not.Null, "Expected template GuardianTransaction type to be available.");
            return type;
        }

        private static object GetFileOperationTypeValue(string memberName)
        {
            Type enumType = GetTemplateGuardianTransactionType().Assembly.GetType(
                "PackageGuardian.Core.Transactions.FileOperationType",
                throwOnError: false);

            Assert.That(enumType, Is.Not.Null, "Expected template FileOperationType enum to be available.");
            return Enum.Parse(enumType, memberName);
        }

        private static object CreateTransaction()
        {
            return Activator.CreateInstance(GetTemplateGuardianTransactionType());
        }

        private static object InvokeTransactionMethod(object transaction, string methodName, params object[] args)
        {
            MethodInfo method = GetTemplateGuardianTransactionType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, $"Expected GuardianTransaction.{methodName} to exist.");
            return method.Invoke(transaction, args);
        }

        private string CreateTextFile(string relativePath, string contents)
        {
            string path = Path.Combine(_workspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _workspaceRoot);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
