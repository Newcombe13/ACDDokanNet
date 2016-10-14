﻿namespace Azi.Cloud.DokanNet.Gui
{
    using System;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Reflection;
    using System.Threading;
    using System.Windows;

    public class ViewModel : INotifyPropertyChanged, IDisposable
    {
        private bool disposedValue = false;
        private Timer refreshTimer;

        public ViewModel()
        {
            if (App != null)
            {
                App.PropertyChanged += App_PropertyChanged;
                refreshTimer = new Timer(RefreshLetters, null, 1000, 1000);

                Clouds.CollectionChanged += Clouds_CollectionChanged;
                UploadFiles.CollectionChanged += UploadFiles_CollectionChanged;
            }
        }

        // To detect redundant calls
        public event PropertyChangedEventHandler PropertyChanged;

        public string CacheFolder
        {
            get
            {
                return Properties.Settings.Default.CacheFolder;
            }

            set
            {
                App.SmallFileCacheFolder = value;
                OnPropertyChanged(nameof(CacheFolder));
            }
        }

        public ObservableCollection<CloudMount> Clouds => App?.Clouds;

        public int DownloadingFilesCount => App?.DownloadingCount ?? 0;

        public bool HasFreeLetters
        {
            get
            {
                return VirtualDriveWrapper.GetFreeDriveLettes().Count > 0;
            }
        }

        public Visibility HasUpdate => App.UpdateAvailable != null ? Visibility.Visible : Visibility.Collapsed;

        public bool IsAutorun
        {
            get
            {
                return App?.GetAutorun() ?? false;
            }

            set
            {
                App.SetAutorun(value);
            }
        }

        public long SmallFilesCacheSize
        {
            get
            {
                return App.SmallFilesCacheSize;
            }

            set
            {
                App.SmallFilesCacheSize = value;
            }
        }

        public long SmallFileSizeLimit
        {
            get
            {
                return App.SmallFileSizeLimit;
            }

            set
            {
                App.SmallFileSizeLimit = value;
            }
        }

        public string UpdateVersion => App.UpdateAvailable?.Version ?? string.Empty;

        public ObservableCollection<FileItemInfo> UploadFiles => App?.UploadFiles;

        public int UploadingFilesCount => App?.UploadingCount ?? 0;

        public string Version => Assembly.GetEntryAssembly().GetName().Version.ToString();

        private App App => App.Current;

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
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
                    refreshTimer.Dispose();
                }

                disposedValue = true;
            }
        }

        private void App_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(App.DownloadingCount):
                    OnPropertyChanged(nameof(DownloadingFilesCount));
                    break;
                case nameof(App.UpdateAvailable):
                    OnPropertyChanged(nameof(HasUpdate));
                    OnPropertyChanged(nameof(UpdateVersion));
                    break;
            }
        }

        private void Clouds_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Clouds));
        }

        private void NotifyMount(string obj)
        {
            RefreshLetters(null);
        }

        private void RefreshLetters(object state)
        {
            if (App == null)
            {
                return;
            }

            foreach (var cloud in Clouds)
            {
                cloud.OnPropertyChanged(nameof(cloud.DriveLetters));
            }

            OnPropertyChanged(nameof(HasFreeLetters));
        }

        private void UploadFiles_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(UploadFiles));
            OnPropertyChanged(nameof(UploadingFilesCount));
        }
    }
}