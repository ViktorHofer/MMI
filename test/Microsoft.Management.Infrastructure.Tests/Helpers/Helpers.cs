﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Management.Infrastructure.Generic;

namespace MMI.Tests
{
    using System.Reflection;
    using System.Runtime.InteropServices;

    public static class Helpers
    {
        private const string testUser_userName = "miDotNetTstUsr";
        private const string testUser_password = "test123.#";
        private static extern int LogonUserA(String lpszUserName,
            String lpszDomain,
            String lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            ref IntPtr phToken);


        private class ObservableToListObserver<T> : IObserver<T>
        {
            private readonly object myLock = new object();
            private readonly List<AsyncItem<T>> results = new List<AsyncItem<T>>();
            private readonly ManualResetEventSlim completeEvent = new ManualResetEventSlim(false);
            private readonly int maxNumberOfResults;

            private int numberOfSimultatenousCallbacks;
            private bool completed;

            internal ObservableToListObserver(int maxNumberOfResults)
            {
                this.maxNumberOfResults = maxNumberOfResults;
            }

            public bool IsSubscription = false;
            private readonly ManualResetEventSlim singleInstanceEvent = new ManualResetEventSlim(false);


            public void OnCompleted()
            {
                try
                {
                    Assert.Equal(
                        Interlocked.Increment(ref this.numberOfSimultatenousCallbacks), 1,
                        "Callbacks should be serialized (i.e. only 1 callback at a time)");

                    lock (this.myLock)
                    {
                        Assert.False(this.completed, "Shouldn't get any OnCompleted callbacks after being in complete state");

                        this.completed = true;
                        this.results.Add(new AsyncItem<T>());
                        this.completeEvent.Set();
                        this.singleInstanceEvent.Set();
                    }
                }
                catch
                {
                    Debug.Assert(false, "No exception should be thrown by the test helper");
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref this.numberOfSimultatenousCallbacks);
                }
            }

            public void OnError(Exception error)
            {
                try
                {
                    Assert.Equal(
                        Interlocked.Increment(ref this.numberOfSimultatenousCallbacks), 1,
                        "Callbacks should be serialized (i.e. only 1 callback at a time)");

                    lock (this.myLock)
                    {
                        Assert.NotNull(error, "error argument of IObserver.OnError should never be null");
                        Assert.False(this.completed, "Shouldn't get any OnError callbacks after being in complete state");

                        this.completed = true;
                        this.results.Add(new AsyncItem<T>(error));
                        this.completeEvent.Set();
                        this.singleInstanceEvent.Set();
                    }
                }
                catch
                {
                    Debug.Assert(false, "No exception should be thrown by the test helper");
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref this.numberOfSimultatenousCallbacks);
                }
            }

            public void OnNext(T value)
            {
                try
                {
                    Assert.Equal(
                        Interlocked.Increment(ref this.numberOfSimultatenousCallbacks), 1,
                        "Callbacks should be serialized (i.e. only 1 callback at a time)");

                    lock (this.myLock)
                    {
                        Assert.False(this.completed, "Shouldn't get any OnNext callbacks after being in complete state");

                        this.results.Add(new AsyncItem<T>(value));
                        Assert.True(this.results.Count <= this.maxNumberOfResults);
                        if (IsSubscription)
                        {
                            this.singleInstanceEvent.Set();
                        }
                    }
                }
                catch
                {
                    Debug.Assert(false, "No exception should be thrown by the test helper");
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref this.numberOfSimultatenousCallbacks);
                }
            }

            public List<AsyncItem<T>> GetResults()
            {
                this.completeEvent.Wait();
                lock (this.myLock)
                {
                    return new List<AsyncItem<T>>(this.results);
                }
            }

            public AsyncItem<T> GetSingleResult()
            {
                this.singleInstanceEvent.Wait();
                lock (this.myLock)
                {
                    List<AsyncItem<T>> availableResults = this.GetAvailableResults();
                    if (availableResults.Count > 0)
                    {
                        return availableResults[0];
                    }
                    return null;
                }
            }

            private List<AsyncItem<T>> GetAvailableResults()
            {
                lock (this.myLock)
                {
                    return new List<AsyncItem<T>>(this.results);
                }
            }

            public void StopProcessingResults()
            {
                lock (this.myLock)
                {
                    this.completeEvent.Set();
                    this.singleInstanceEvent.Set();
                }
            }
        }


        private readonly static BindingFlags PrivateBindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static Y GetPrivateProperty<X, Y>(this X self, string name)
        {
            object[] emptyArgs = new object[] { };
            var property = typeof(X).GetProperty(name, PrivateBindingFlags);
            return (Y)property.GetMethod.Invoke(self, emptyArgs);
        }

        public static Y GetPrivateVariable<X, Y>(this X self, string name)
        {
            return (Y)typeof(X).GetField(name, PrivateBindingFlags).GetValue(self);
        }

        public static string GetStringRepresentationOfSerializedData(byte[] data)
        {
#if !_CORECLR
            return Encoding.Unicode.GetString(data);
#else
            return Encoding.ASCII.GetString(data);
#endif
        }

        /// <summary>
        /// convert string to byte[]
        /// </summary>
        /// <returns></returns>
        public static byte[] GetBytesFromString(string str)
        {
            System.Text.UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(str);
        }

        /// <summary>
        /// Read file content to byte[]
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static byte[] GetBytesFromFile(string filePath)
        {
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] bytes = new byte[fs.Length];
                fs.Read(bytes, 0, Convert.ToInt32(fs.Length));
                // FileStream.close method is not supported in .net core currently.
#if !_LINUX
                fs.Close();
#else
#endif
                return bytes;
            }
        }

        public static void AssertException<T>(Action exceptionCausingAction, Action<T> exceptionVerificationAction = null) where T : Exception
        {
            try
            {
                exceptionCausingAction();
                Assert.True(false, "Expected " + typeof(T).Name + " exception");
            }
            catch (T exception)
            {
                if (exceptionVerificationAction != null)
                {
                    exceptionVerificationAction(exception);
                }
            }
        }

        public static void AssertRunningAsNonTestUser(string message)
        {
            string testUser_userName = "miDotNetTstUsr";
            Assert.NotEqual(WindowsIdentity.GetCurrent().Name, Environment.MachineName + "\\" + testUser_userName, "Asserting that we are not running as impersonated user (windows identity): " + message);
            object logicalContextData = CallContext.LogicalGetData("ImpersonationTest");
            Assert.Null(logicalContextData, "Asserting that we are not running as impersonated user (logicalcallcontext-null): " + message);
        }

        public static void AssertRunningAsTestUser(string message)
        {
            string testUser_userName = "miDotNetTstUsr";
            Assert.Equal(WindowsIdentity.GetCurrent().Name, Environment.MachineName + "\\" + testUser_userName, "Asserting that we are running as impersonated user (windowsidentity): " + message);
            object logicalContextData = CallContext.LogicalGetData("ImpersonationTest");
            Assert.NotNull(logicalContextData, "Asserting that we are running as impersonated user (logicalcallcontext-notnull): " + message);
            Assert.Equal(logicalContextData.ToString(), Process.GetCurrentProcess().Id.ToString(), "Asserting that we are running as impersonated user (logicalcallcontext-datacontent): " + message);
        }

        private static void AddTestUser()
        {
            DirectoryEntry computer = new DirectoryEntry("WinNT://" + Environment.MachineName + ",computer");
            if (computer.Children.Cast<DirectoryEntry>().Any(entry => entry.Name.Equals(testUser_userName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            DirectoryEntry user = computer.Children.Add(testUser_userName, "user");
            user.Invoke("SetPassword", new object[] { testUser_password });
            user.Invoke("Put", new object[] { "Description", "Test user for MI Client .NET API DRTs" });
            user.CommitChanges();

            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            string name = sid.Translate(typeof(NTAccount)).ToString();
            string administratorsGroupName = name.Split('\\')[1];

            DirectoryEntry group = computer.Children.Find(administratorsGroupName, "group");
            group.Invoke("Add", new object[] { user.Path.ToString() });
        }

        private static IntPtr LogUser(string strUsername, string strDomain, string strPassword)
        {
            IntPtr tokenHandle = new IntPtr(0);

            const int LOGON32_PROVIDER_DEFAULT = 0;
            //This parameter causes LogonUser to create a primary token.
            const int LOGON32_LOGON_INTERACTIVE = 2;

            tokenHandle = IntPtr.Zero;

            // Call LogonUser to obtain a handle to an access token.
            int returnValue = LogonUserA(
                strUsername,
                strDomain,
                strPassword,
                LOGON32_LOGON_INTERACTIVE,
                LOGON32_PROVIDER_DEFAULT,
                ref tokenHandle);

            if (0 == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                return IntPtr.Zero;
            }

            WindowsIdentity mWI1 = WindowsIdentity.GetCurrent();
            IntPtr token2 = new IntPtr(tokenHandle.ToInt32());

            return token2;
        }

        private class MyImpersonationRestorationDisposable : IDisposable
        {
            public MyImpersonationRestorationDisposable(WindowsImpersonationContext impersonationContext)
            {
                this._impersonationContext = impersonationContext;
            }

            private WindowsImpersonationContext _impersonationContext;

            public void Dispose()
            {
                this._impersonationContext.Undo();
                CallContext.FreeNamedDataSlot("ImpersonationTest");
            }
        }

        public static IDisposable ImpersonateTestUser()
        {
            AddTestUser();

            IntPtr token = LogUser(testUser_userName, Environment.MachineName, testUser_password);
            WindowsIdentity mWI2 = new WindowsIdentity(token);
            WindowsPrincipal winPrin = new WindowsPrincipal(mWI2);
            Thread.CurrentPrincipal = winPrin;
            WindowsImpersonationContext impersonationContext = mWI2.Impersonate();
            CallContext.LogicalSetData("ImpersonationTest", Process.GetCurrentProcess().Id);

            IDisposable restorationToken = new MyImpersonationRestorationDisposable(impersonationContext);
            return restorationToken;
        }

        public static List<AsyncItem<T>> ObservableToList<T>(IObservable<T> observable)
        {
            return ObservableToList(observable, null);
        }

        public static AsyncItem<T> ObservableToSingleItem<T>(IObservable<T> observable)
        {
            Assert.NotNull(observable, "API should never return a null observable");

            int maxNumberOfResults = int.MaxValue;
            if (observable.GetType().IsGenericType && observable.GetType().GetGenericTypeDefinition().Equals(typeof(CimAsyncResult<>)))
            {
                maxNumberOfResults = 1;
            }

            var observer = new ObservableToListObserver<T>(maxNumberOfResults);
            observer.IsSubscription = true;

            IDisposable cancellationDisposable = null;
            try
            {
                cancellationDisposable = observable.Subscribe(observer);
            }
            catch
            {
                Assert.True(false, "Subscribe should not throw any exceptions");
            }
            Assert.NotNull(
                cancellationDisposable,
                "Wes Dyer says that Subscribe should never return null (even for non-cancellable operations) - this results in better composability");

            Thread.Sleep(3000);
            CimTestFixture.StartDummyProcess();
            return observer.GetSingleResult();
        }

        public static List<AsyncItem<T>> ObservableToList<T>(IObservable<T> observable, TimeSpan? cancellationDelay)
        {
            Assert.NotNull(observable, "API should never return a null observable");

            int maxNumberOfResults = int.MaxValue;
            if (observable.GetType().IsGenericType && observable.GetType().GetGenericTypeDefinition().Equals(typeof(CimAsyncResult<>)))
            {
                maxNumberOfResults = 1;
            }

            var observer = new ObservableToListObserver<T>(maxNumberOfResults);

            IDisposable cancellationDisposable = null;
            try
            {
                cancellationDisposable = observable.Subscribe(observer);
            }
            catch
            {
                Assert.True(false, "Subscribe should not throw any exceptions");
            }
            Assert.NotNull(
                cancellationDisposable,
                "Wes Dyer says that Subscribe should never return null (even for non-cancellable operations) - this results in better composability");

            if (cancellationDelay.HasValue)
            {
                Assert.NotNull(cancellationDisposable, "IObservable.Subscribe should always return non-null");
                if (cancellationDelay != TimeSpan.MaxValue)
                {
                    Thread.Sleep((int)(cancellationDelay.Value.TotalMilliseconds));
                    cancellationDisposable.Dispose();
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                    Thread.Sleep((int)(cancellationDelay.Value.TotalMilliseconds));
                    observer.StopProcessingResults();
                }
            }

            List<AsyncItem<T>> results = observer.GetResults();

            if (cancellationDelay.HasValue)
            {
                if (cancellationDelay == TimeSpan.MaxValue)
                {
                    cancellationDisposable.Dispose();
                }
            }

            return results;
        }
    }

    public enum AsyncItemKind
    {
        Item,
        Exception,
        Completion
    }

    public class AsyncItem<T>
    {
        public AsyncItem(T item)
        {
            this.Kind = AsyncItemKind.Item;
            this.Item = item;
            this.Timestamp = DateTime.Now;
        }

        public AsyncItem(Exception ex)
        {
            this.Kind = AsyncItemKind.Exception;
            this.Exception = ex;
            this.Timestamp = DateTime.Now;
        }

        public AsyncItem()
        {
            this.Kind = AsyncItemKind.Completion;
            this.Timestamp = DateTime.Now;
        }

        public AsyncItemKind Kind { get; private set; }
        public T Item { get; private set; }
        public Exception Exception { get; private set; }
        public DateTime Timestamp { get; private set; }
    }

    public class CimImpersonationHelpers
    {
        [DllImport("advapi32.dll")]
        private static extern int LogonUserA(String lpszUserName,
            String lpszDomain,
            String lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            ref IntPtr phToken);

        private static IntPtr LogUser(string strUsername, string strDomain, string strPassword)
        {
            IntPtr tokenHandle = new IntPtr(0);

            const int LOGON32_PROVIDER_DEFAULT = 0;
            //This parameter causes LogonUser to create a primary token.
            const int LOGON32_LOGON_INTERACTIVE = 2;

            tokenHandle = IntPtr.Zero;

            // Call LogonUser to obtain a handle to an access token.
            int returnValue = LogonUserA(
                strUsername,
                strDomain,
                strPassword,
                LOGON32_LOGON_INTERACTIVE,
                LOGON32_PROVIDER_DEFAULT,
                ref tokenHandle);

            if (0 == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                return IntPtr.Zero;
            }

            WindowsIdentity mWI1 = WindowsIdentity.GetCurrent();
            IntPtr token2 = new IntPtr(tokenHandle.ToInt32());

            return token2;
        }

        private class MyImpersonationRestorationDisposable : IDisposable
        {
            public MyImpersonationRestorationDisposable(WindowsImpersonationContext impersonationContext)
            {
                this._impersonationContext = impersonationContext;
            }

            private WindowsImpersonationContext _impersonationContext;

            public void Dispose()
            {
                this._impersonationContext.Undo();
                CallContext.FreeNamedDataSlot("ImpersonationTest");
            }
        }

        private const string testUser_userName = "miDotNetTstUsr";
        private const string testUser_password = "test123.#";

        private static void AddTestUser()
        {
            DirectoryEntry computer = new DirectoryEntry("WinNT://" + Environment.MachineName + ",computer");
            if (computer.Children.Cast<DirectoryEntry>().Any(entry => entry.Name.Equals(testUser_userName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            DirectoryEntry user = computer.Children.Add(testUser_userName, "user");
            user.Invoke("SetPassword", new object[] { testUser_password });
            user.Invoke("Put", new object[] { "Description", "Test user for MI Client .NET API DRTs" });
            user.CommitChanges();

            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            string name = sid.Translate(typeof(NTAccount)).ToString();
            string administratorsGroupName = name.Split('\\')[1];

            DirectoryEntry group = computer.Children.Find(administratorsGroupName, "group");
            group.Invoke("Add", new object[] { user.Path.ToString() });
        }

        public static IDisposable ImpersonateTestUser()
        {
            AddTestUser();

            IntPtr token = LogUser(testUser_userName, Environment.MachineName, testUser_password);
            WindowsIdentity mWI2 = new WindowsIdentity(token);
            WindowsPrincipal winPrin = new WindowsPrincipal(mWI2);
            Thread.CurrentPrincipal = winPrin;
            WindowsImpersonationContext impersonationContext = mWI2.Impersonate();
            CallContext.LogicalSetData("ImpersonationTest", Process.GetCurrentProcess().Id);

            IDisposable restorationToken = new MyImpersonationRestorationDisposable(impersonationContext);
            return restorationToken;
        }

        public static void AssertRunningAsTestUser(string message)
        {
            Assert.Equal(WindowsIdentity.GetCurrent().Name, Environment.MachineName + "\\" + testUser_userName, "Asserting that we are running as impersonated user (windowsidentity): " + message);

            object logicalContextData = CallContext.LogicalGetData("ImpersonationTest");
            Assert.NotNull(logicalContextData, "Asserting that we are running as impersonated user (logicalcallcontext-notnull): " + message);
            Assert.Equal(logicalContextData.ToString(), Process.GetCurrentProcess().Id.ToString(), "Asserting that we are running as impersonated user (logicalcallcontext-datacontent): " + message);
        }

        public static void AssertRunningAsNonTestUser(string message)
        {
            Assert.NotEqual(WindowsIdentity.GetCurrent().Name, Environment.MachineName + "\\" + testUser_userName, "Asserting that we are not running as impersonated user (windows identity): " + message);

            object logicalContextData = CallContext.LogicalGetData("ImpersonationTest");
            Assert.Null(logicalContextData, "Asserting that we are not running as impersonated user (logicalcallcontext-null): " + message);
        }
    }


}
