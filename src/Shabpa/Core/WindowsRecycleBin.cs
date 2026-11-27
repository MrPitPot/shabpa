using System.Runtime.InteropServices;

namespace Shabpa.Core;

internal static class WindowsRecycleBin
{
    private const uint RecycleOnDelete = 0x00080000;
    private const uint EarlyFailure = 0x00100000;
    private const uint NoErrorUi = 0x0400;
    private const uint Silent = 0x0004;
    private const uint DeleteRecycleIfPossible = 0x80;
    private const int Abort = unchecked((int)0x80004004);

    internal static string? Move(string path)
    {
        var itemId = typeof(IShellItem).GUID;
        var hr = SHCreateItemFromParsingName(path, 0, in itemId, out var item);
        if (hr < 0) return Error(hr);

        IFileOperation? operation = null;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!)!;
            operation.SetOperationFlags(RecycleOnDelete | EarlyFailure | NoErrorUi | Silent);
            var progress = new DeleteProgress();
            operation.DeleteItem(item, progress);
            hr = operation.PerformOperations();
            if (progress.Recycled) return null;
            if (progress.Refused) return "recycle bin unavailable";
            if (progress.Failure is int failure) return Error(failure);
            return hr < 0 ? Error(hr) : "recycle bin unavailable";
        }
        catch (COMException ex)
        {
            return Error(ex.HResult);
        }
        finally
        {
            if (operation is not null) Marshal.ReleaseComObject(operation);
            Marshal.ReleaseComObject(item);
        }
    }

    internal static string Error(int hr) => (uint)hr switch
    {
        0x80070002 or 0x80070003 or 0x80270023 => "no longer exists",
        0x80070005 or 0x80270021 or 0x80270022 or 0x8027003F => "access denied",
        0x80070020 or 0x80070021 or 0x80270027 or 0x80270028 => "file in use",
        0x800700CE or 0x8027001D or 0x8027001E or 0x80270038 => "path too long",
        0x80270037 => "too large for recycle bin",
        0x80270035 or 0x80270036 or 0x8027003A => "recycle bin unavailable",
        _ => $"0x{hr:X8}",
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid itemId, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        // Keep every slot up to PerformOperations in native vtable order.
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(IShellItem item, nint name, nint sink);
        void RenameItems(nint items, nint name);
        void MoveItem(IShellItem item, IShellItem destination, nint name, nint sink);
        void MoveItems(nint items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, nint name, nint sink);
        void CopyItems(nint items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems(nint items);
        void NewItem(IShellItem destination, uint attributes, nint name, nint template, nint sink);
        [PreserveSig] int PerformOperations();
    }

    [ComImport, Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, nint name);
        [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint destination, nint name);
        [PreserveSig] int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeleteProgress : IFileOperationProgressSink
    {
        public bool Recycled { get; private set; }
        public bool Refused { get; private set; }
        public int? Failure { get; private set; }

        public int PreDeleteItem(uint flags, nint item)
        {
            // A shell fallback to permanent deletion must be stopped before it starts.
            Refused = (flags & DeleteRecycleIfPossible) == 0;
            return Refused ? Abort : 0;
        }

        public int PostDeleteItem(uint flags, nint item, int result, nint created)
        {
            // The shell returns the created Recycle Bin item only for a recycled file.
            Recycled = result >= 0 && created != 0;
            if (result < 0) Failure = result;
            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, nint item, nint name) => 0;
        public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreCopyItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreNewItem(uint flags, nint destination, nint name) => 0;
        public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
