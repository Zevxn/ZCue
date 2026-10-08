using System.Runtime.InteropServices;

namespace ZCue.Infrastructure;

// WPF 的托管 UIA 包装没有 TextEditPattern；以下接口按 Windows SDK 的虚表顺序声明。
internal static class TextEditInterop
{
    internal const int TextEditPatternId = 10032;
    internal const int ProcessIdPropertyId = 30002;

    internal static IAutomation CreateClient() => (IAutomation)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("E22AD333-B25F-460C-83D0-0581107395C9"), throwOnError: true)!)!;

    // SECTION UI Automation 原生接口

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAutomation
    {
        int CompareElements(IElement first, IElement second);
        int CompareRuntimeIds([MarshalAs(UnmanagedType.SafeArray)] Array first,
            [MarshalAs(UnmanagedType.SafeArray)] Array second);
        IElement GetRootElement();
        IElement ElementFromHandle(IntPtr window);
        IElement ElementFromPoint(NativeMethods.Point point);
        IElement GetFocusedElement();
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElement
    {
        void SetFocus();
        [return: MarshalAs(UnmanagedType.SafeArray)]
        Array GetRuntimeId();
        IElement FindFirst(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        [return: MarshalAs(UnmanagedType.Interface)]
        object FindAll(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        IElement FindFirstBuildCache(int scope, [MarshalAs(UnmanagedType.Interface)] object condition,
            [MarshalAs(UnmanagedType.Interface)] object cacheRequest);
        [return: MarshalAs(UnmanagedType.Interface)]
        object FindAllBuildCache(int scope, [MarshalAs(UnmanagedType.Interface)] object condition,
            [MarshalAs(UnmanagedType.Interface)] object cacheRequest);
        IElement BuildUpdatedCache([MarshalAs(UnmanagedType.Interface)] object cacheRequest);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValueEx(int propertyId, int ignoreDefaultValue);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValue(int propertyId);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValueEx(int propertyId, int ignoreDefaultValue);
        IntPtr GetCurrentPatternAs(int patternId, ref Guid interfaceId);
        IntPtr GetCachedPatternAs(int patternId, ref Guid interfaceId);
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("17E21576-996C-4870-99D9-BFF323380C06")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextEditPattern
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object RangeFromPoint(NativeMethods.Point point);
        [return: MarshalAs(UnmanagedType.Interface)]
        object RangeFromChild(IElement child);
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetSelection();
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetVisibleRanges();
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetDocumentRange();
        int GetSupportedTextSelection();
        [return: MarshalAs(UnmanagedType.Interface)]
        object? GetActiveComposition();
        [return: MarshalAs(UnmanagedType.Interface)]
        object? GetConversionTarget();
    }

    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextRange
    {
        ITextRange Clone();
        int Compare(ITextRange other);
        int CompareEndpoints(int endpoint, ITextRange other, int otherEndpoint);
        void ExpandToEnclosingUnit(int unit);
        ITextRange FindAttribute(int attributeId, [MarshalAs(UnmanagedType.Struct)] object value, int backward);
        ITextRange FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetAttributeValue(int attributeId);
        [return: MarshalAs(UnmanagedType.SafeArray)]
        Array GetBoundingRectangles();
        IElement GetEnclosingElement();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maxLength);
    }

    // !SECTION UI Automation 原生接口
}
