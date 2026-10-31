using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace FileTagger
{
    /// <summary>
    /// Out-of-process COM server behind the "Manage Tags" Explorer verb. Explorer hides legacy
    /// command-line verbs when more than 15 (or 100 with MultiSelectModel=Player) files are selected
    /// and starts one process per file; a DropTarget verb has no limit and receives the whole
    /// selection in a single Drop call.
    /// </summary>
    public static class ShellDropTarget
    {
        public const string ManageTagsClsid = "{6B0F4C1E-5D7A-4E8B-9C21-3A4F2E7D9B10}";

        private const uint CLSCTX_LOCAL_SERVER = 0x4;
        private const uint REGCLS_MULTIPLEUSE = 1;
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

        public static bool IsEmbeddingLaunch(string[] args) =>
            args.Length == 1 &&
            (string.Equals(args[0], "-Embedding", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "/Embedding", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Registers the class factory on the calling (STA, dispatcher) thread. <paramref name="onFiles"/>
        /// runs on that dispatcher once Explorer drops the selection; <paramref name="onIdle"/> runs if
        /// nothing arrives in time.
        /// </summary>
        public static void RunServer(Action<List<string>> onFiles, Action onIdle)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            uint cookie = 0;
            var handled = false;

            void Revoke()
            {
                if (cookie != 0)
                {
                    CoRevokeClassObject(cookie);
                    cookie = 0;
                }
            }

            var idleTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = IdleTimeout };
            idleTimer.Tick += (_, __) =>
            {
                idleTimer.Stop();
                if (handled)
                    return;
                Revoke();
                onIdle();
            };

            var factory = new DropTargetClassFactory(files =>
            {
                // Return from Explorer's synchronous Drop call before doing any work.
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (handled)
                        return;
                    handled = true;
                    idleTimer.Stop();
                    Revoke();
                    onFiles(files);
                }));
            });

            var clsid = new Guid(ManageTagsClsid);
            var hr = CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            idleTimer.Start();
        }

        internal static List<string> ReadFilePaths(ComTypes.IDataObject dataObject)
        {
            var paths = new List<string>();
            var format = new ComTypes.FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = ComTypes.TYMED.TYMED_HGLOBAL
            };

            dataObject.GetData(ref format, out var medium);
            try
            {
                var hDrop = medium.unionmember;
                var count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var length = DragQueryFile(hDrop, i, null, 0);
                    var buffer = new StringBuilder((int)length + 1);
                    DragQueryFile(hDrop, i, buffer, (uint)buffer.Capacity);
                    paths.Add(buffer.ToString());
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }

            return paths;
        }

        private const short CF_HDROP = 15;

        [DllImport("ole32.dll")]
        private static extern int CoRegisterClassObject(ref Guid rclsid,
            [MarshalAs(UnmanagedType.IUnknown)] object pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

        [DllImport("ole32.dll")]
        private static extern int CoRevokeClassObject(uint dwRegister);

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);

        [PreserveSig]
        int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class ManageTagsDropTarget : IDropTarget
    {
        private const uint DROPEFFECT_COPY = 1;
        private const int S_OK = 0;
        private const int E_FAIL = unchecked((int)0x80004005);

        private readonly Action<List<string>> _onFiles;

        public ManageTagsDropTarget(Action<List<string>> onFiles) => _onFiles = onFiles;

        public int DragEnter(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = DROPEFFECT_COPY;
            return S_OK;
        }

        public int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = DROPEFFECT_COPY;
            return S_OK;
        }

        public int DragLeave() => S_OK;

        public int Drop(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            try
            {
                var files = ShellDropTarget.ReadFilePaths(pDataObj);
                pdwEffect = DROPEFFECT_COPY;
                _onFiles(files);
                return S_OK;
            }
            catch
            {
                return E_FAIL;
            }
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class DropTargetClassFactory : IClassFactory
    {
        private const int S_OK = 0;
        private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

        private readonly Action<List<string>> _onFiles;

        public DropTargetClassFactory(Action<List<string>> onFiles) => _onFiles = onFiles;

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero)
                return CLASS_E_NOAGGREGATION;

            var unknown = Marshal.GetIUnknownForObject(new ManageTagsDropTarget(_onFiles));
            try
            {
                return Marshal.QueryInterface(unknown, ref riid, out ppvObject);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public int LockServer(bool fLock) => S_OK;
    }
}
