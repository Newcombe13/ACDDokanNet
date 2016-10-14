﻿namespace Azi.Cloud.DokanNet.Gui
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using Common;
    using DokanNet;
    using Tools;

    public class CloudMount : INotifyPropertyChanged, IDisposable, IAuthUpdateListener
    {
        private readonly CloudInfo cloudInfo;

        private bool disposedValue = false;
        private IHttpCloud instance;
        private bool mounting = false;
        private bool unmounting = false;
        private ManualResetEventSlim unmountingEvent;

        public CloudMount(CloudInfo info)
        {
            cloudInfo = info;
            cloudInfo.PropertyChanged += CloudInfoChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool CanMount => (Instance != null) && (!mounting) && !(MountLetter != null);

        public bool CanUnmount => (!unmounting) && (MountLetter != null);

        public CloudInfo CloudInfo
        {
            get
            {
                return cloudInfo;
            }
        }

        public string CloudServiceIcon => Instance?.CloudServiceIcon ?? "images/lib_load_error.png";

        public IList<char> DriveLetters
        {
            get
            {
                var res = VirtualDriveWrapper.GetFreeDriveLettes();

                if (MountLetter != null && !res.Contains((char)MountLetter))
                {
                    res.Add((char)MountLetter);
                }
                else
                if (MountLetter == null && (mounting || unmounting) && !res.Contains(CloudInfo.DriveLetter))
                {
                    res.Add(CloudInfo.DriveLetter);
                }
                else
                {
                    return res;
                }

                return res.OrderBy(c => c).ToList();
            }
        }

        public IHttpCloud Instance
        {
            get
            {
                try
                {
                    if (instance == null)
                    {
                        instance = CreateInstance();
                        instance.Id = CloudInfo.Id;
                    }

                    return instance;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    return null;
                }
            }
        }

        public bool IsMounted => !mounting && !unmounting && (MountLetter != null);

        public bool IsUnmounted => !unmounting && !unmounting && !(MountLetter != null);

        public CancellationTokenSource MountCancellation { get; } = new CancellationTokenSource();

        public char? MountLetter { get; set; }

        public Visibility MountVisible => (!unmounting && (MountLetter == null)) ? Visibility.Visible : Visibility.Collapsed;

        public FSProvider Provider { get; private set; }

        public Visibility UnmountVisible => (!mounting && (MountLetter != null)) ? Visibility.Visible : Visibility.Collapsed;

        private App App => App.Current;

        public async Task Delete()
        {
            await UnmountAsync();
            if (Instance != null)
            {
                await Instance.SignOut(CloudInfo.AuthSave);
            }

            App.DeleteCloud(this);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        public async Task MountAsync(bool interactiveAuth = true)
        {
            if (App == null)
            {
                throw new NullReferenceException();
            }

            mounting = true;
            NotifyMount();
            try
            {
                try
                {
                    MountLetter = await Mount(interactiveAuth);
                }
                catch (TimeoutException)
                {
                    // Ignore if timeout
                }
                catch (OperationCanceledException)
                {
                    // Ignore if aborted
                }
            }
            finally
            {
                mounting = false;
                NotifyMount();
            }
        }

        public void OnAuthUpdated(IHttpCloud sender, string authinfo)
        {
            CloudInfo.AuthSave = authinfo;
            App.SaveClouds();
        }

        public async Task UnmountAsync()
        {
            if (MountLetter == null)
            {
                return;
            }

            if (App == null)
            {
                throw new NullReferenceException();
            }

            unmounting = true;
            NotifyMount();
            try
            {
                await Task.Factory.StartNew(() =>
                {
                    using (unmountingEvent = new ManualResetEventSlim(false))
                    {
                        VirtualDriveWrapper.Unmount((char)MountLetter);
                        unmountingEvent.Wait();
                    }
                });

                MountLetter = null;
                Provider.Stop();
                App.NotifyUnmount(cloudInfo.Id);
            }
            finally
            {
                unmounting = false;
                NotifyMount();
            }
        }

        internal void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    MountCancellation.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                disposedValue = true;
            }
        }

        private async Task<bool> Authenticate(IHttpCloud cloud, CancellationToken cs, bool interactiveAuth)
        {
            var authinfo = CloudInfo.AuthSave;
            if (!string.IsNullOrWhiteSpace(authinfo))
            {
                if (await cloud.AuthenticateSaved(cs, authinfo))
                {
                    return true;
                }
            }

            Debug.WriteLine("No auth info: " + CloudInfo.Name);
            if (!interactiveAuth)
            {
                return false;
            }

            return await cloud.AuthenticateNew(cs);
        }

        private void CloudInfoChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CloudInfo));
            if (Provider != null)
            {
                Provider.VolumeName = CloudInfo.Name;
            }

            App.SaveClouds();
        }

        private IHttpCloud CreateInstance()
        {
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CloudInfo.AssemblyFileName));

            var types = assembly.GetExportedTypes().Where(t => typeof(IHttpCloud).IsAssignableFrom(t));

            var assemblyName = types.Single(t => t.IsClass).Assembly.FullName;

            return Activator.CreateInstance(assemblyName, CloudInfo.ClassName).Unwrap() as IHttpCloud;
        }

        private async Task<char> Mount(bool interactiveAuth = true)
        {
            try
            {
                Instance.OnAuthUpdated = this;
                var authenticated = await Authenticate(instance, MountCancellation.Token, interactiveAuth);

                if (!authenticated)
                {
                    Log.Error("Authentication failed");
                    throw new InvalidOperationException("Authentication failed");
                }

                Provider = new FSProvider(instance, ProviderStatisticsUpdated);
                Provider.VolumeName = CloudInfo.Name;
                Provider.CachePath = Environment.ExpandEnvironmentVariables(Properties.Settings.Default.CacheFolder);
                Provider.SmallFilesCacheSize = Properties.Settings.Default.SmallFilesCacheLimit * (1 << 20);
                Provider.SmallFileSizeLimit = Properties.Settings.Default.SmallFileSizeLimit * (1 << 20);

                var cloudDrive = new VirtualDriveWrapper(Provider);

                var mountedEvent = new TaskCompletionSource<char>();

                cloudDrive.Mounted = (letter) =>
                {
                    mountedEvent.SetResult(letter);
                };

                NotifyMount();

                var task = Task.Run(() =>
                {
                    try
                    {
                        cloudDrive.Mount(CloudInfo.DriveLetter, CloudInfo.ReadOnly);
                        unmountingEvent.Set();
                    }
                    catch (InvalidOperationException)
                    {
                        Log.Warn($"Drive letter {CloudInfo.DriveLetter} is already used");
                        Exception lastException = null;
                        bool wasMounted = false;
                        foreach (char letter in VirtualDriveWrapper.GetFreeDriveLettes())
                        {
                            try
                            {
                                cloudDrive.Mount(letter, CloudInfo.ReadOnly);
                                unmountingEvent.Set();

                                wasMounted = true;
                                break;
                            }
                            catch (InvalidOperationException ex)
                            {
                                lastException = ex;
                                Log.Warn($"Drive letter {letter} is already used");
                            }
                        }

                        if (!wasMounted)
                        {
                            var message = "Could not find free letter";
                            if (lastException != null && lastException.InnerException != null)
                            {
                                message = lastException.InnerException.Message;
                            }

                            mountedEvent.SetException(new InvalidOperationException(message));
                        }
                    }
                    catch (Exception ex)
                    {
                        mountedEvent.SetException(ex);
                    }
                });
                return await mountedEvent.Task;
            }
            finally
            {
                NotifyMount();
            }
        }

        private void NotifyMount()
        {
            OnPropertyChanged(nameof(MountVisible));
            OnPropertyChanged(nameof(UnmountVisible));
            OnPropertyChanged(nameof(CanMount));
            OnPropertyChanged(nameof(CanUnmount));
            OnPropertyChanged(nameof(IsMounted));
            OnPropertyChanged(nameof(IsUnmounted));
        }

        private void ProviderStatisticsUpdated(IHttpCloud cloud, StatisticUpdateReason reason, AStatisticFileInfo info)
        {
            App?.OnProviderStatisticsUpdated(CloudInfo, reason, info);
        }
    }
}