using System.Runtime.InteropServices;

namespace ZCue.Infrastructure;

// SECTION 原生 UI Automation 光标接口

// .NET 的 UIAutomationClient 没有封装 TextPattern2。接口前缀按 Windows SDK
// UIAutomationClient.idl 的顺序声明；未调用的前置成员也必须保留 COM 槽位。
internal static class UiAutomationInterop
{
    internal static readonly Guid ClassId = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    internal const int TextPattern2Id = 10024;
    internal const int ProcessIdProperty = 30002;

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAutomation
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool CompareElements(IElement first, IElement second);
        [return: MarshalAs(UnmanagedType.Bool)]
        bool CompareRuntimeIds(
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] first,
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] second);
        IElement GetRootElement();
        IElement ElementFromHandle(IntPtr window);
        IElement ElementFromPoint(NativeMethods.Point point);
        IElement GetFocusedElement();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElement
    {
        void SetFocus();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
        int[] GetRuntimeId();
        IElement FindFirst(int scope, IntPtr condition);
        IntPtr FindAll(int scope, IntPtr condition);
        IElement FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);
        IntPtr FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);
        IElement BuildUpdatedCache(IntPtr cacheRequest);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault);
        IntPtr GetCurrentPatternAs(int patternId, ref Guid interfaceId);
        IntPtr GetCachedPatternAs(int patternId, ref Guid interfaceId);
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextPattern2
    {
        ITextRange RangeFromPoint(NativeMethods.Point point);
        ITextRange RangeFromChild(IElement child);
        IntPtr GetSelection();
        IntPtr GetVisibleRanges();
        ITextRange GetDocumentRange();
        int GetSupportedTextSelection();
        ITextRange RangeFromAnnotation(IElement annotation);
        ITextRange GetCaretRange([MarshalAs(UnmanagedType.Bool)] out bool isActive);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextRange
    {
        ITextRange Clone();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool Compare(ITextRange range);
        int CompareEndpoints(int endpoint, ITextRange range, int otherEndpoint);
        void ExpandToEnclosingUnit(int unit);
        ITextRange FindAttribute(int attribute, [MarshalAs(UnmanagedType.Struct)] object value,
            [MarshalAs(UnmanagedType.Bool)] bool backward);
        ITextRange FindText([MarshalAs(UnmanagedType.BStr)] string text,
            [MarshalAs(UnmanagedType.Bool)] bool backward, [MarshalAs(UnmanagedType.Bool)] bool ignoreCase);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetAttributeValue(int attribute);
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)]
        double[] GetBoundingRectangles();
        IElement GetEnclosingElement();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maxLength);
        int Move(int unit, int count);
        int MoveEndpointByUnit(int endpoint, int unit, int count);
        void MoveEndpointByRange(int endpoint, ITextRange range, int otherEndpoint);
    }

    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}

// !SECTION 原生 UI Automation 光标接口
