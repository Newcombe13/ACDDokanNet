﻿using Azi.Amazon.CloudDrive;
using Azi.Amazon.CloudDrive.JsonObjects;
using Azi.Tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;

namespace Azi.ACDDokanNet
{
    public interface IBlockStream : IDisposable
    {
        int Read(long position, byte[] buffer, int offset, int count, int timeout = 1000);
        void Write(long position, byte[] buffer, int offset, int count, int timeout = 1000);
        void Close();
        void Flush();

        Action OnClose { get; set; }
    }

    public abstract class AbstractBlockStream : IBlockStream
    {
        public abstract void Flush();
        public abstract int Read(long position, byte[] buffer, int offset, int count, int timeout = 1000);
        public abstract void Write(long position, byte[] buffer, int offset, int count, int timeout = 1000);

        public Action OnClose { get; set; }

        public virtual void Close()
        {
            OnClose?.Invoke();
        }

        protected abstract void Dispose(bool disposing);

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }

}