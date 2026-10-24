using System;
using System.Runtime.InteropServices;

public static class KeyboardImageFileDialog
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int FILE_PATH_CAPACITY = 32768;
    private const int OPEN_FLAGS = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        private int _size;
        private IntPtr _owner;
        private IntPtr _instance;
        private IntPtr _filter;
        private IntPtr _customFilter;
        private int _maxCustomFilter;
        private int _filterIndex;
        private IntPtr _file;
        private int _maxFile;
        private IntPtr _fileTitle;
        private int _maxFileTitle;
        private IntPtr _initialDirectory;
        private IntPtr _title;
        private int _flags;
        private short _fileOffset;
        private short _extensionOffset;
        private IntPtr _defaultExtension;
        private IntPtr _customData;
        private IntPtr _hook;
        private IntPtr _templateName;
        private IntPtr _reserved;
        private int _reservedValue;
        private int _extendedFlags;

        /// <summary>
        /// 파일 경로 버퍼, 필터와 제목 포인터로 Windows 파일 선택 설정을 구성한다.
        /// 현재 창을 소유자로 지정하고 기존 이미지 파일 한 개만 선택하도록 설정한다.
        /// </summary>
        public OpenFileName(IntPtr file, IntPtr filter, IntPtr title)
        {
            this = default;
            _size = Marshal.SizeOf(typeof(OpenFileName));
            _owner = GetActiveWindow();
            _filter = filter;
            _filterIndex = 1;
            _file = file;
            _maxFile = FILE_PATH_CAPACITY;
            _title = title;
            _flags = OPEN_FLAGS;
        }
    }

    /// <summary>settings로 Windows 파일 선택 창을 열고 선택 성공 여부를 반환한다. 선택 경로를 settings의 버퍼에 기록한다.</summary>
    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName settings);

    /// <summary>입력 없이 마지막 공통 대화상자의 오류 코드를 반환한다. 취소는 0을 반환한다.</summary>
    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern uint CommDlgExtendedError();

    /// <summary>입력 없이 현재 스레드의 활성 창 핸들을 반환해 파일 선택 창의 소유자로 사용한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();
#endif

    /// <summary>
    /// 입력 없이 PNG와 JPEG 이미지 파일 선택 창을 열고 선택한 절대 경로를 반환한다.
    /// 취소하면 빈 문자열을 반환하고 지원하지 않는 플랫폼이나 대화상자 오류는 예외로 알린다.
    /// </summary>
    public static string ChooseImage()
    {
        using (InGameCursor.SuspendForSystemDialog())
            return ChooseImageInternal();
    }

    /// <summary>현재 플랫폼의 파일 선택창을 열어 선택 경로를 반환한다. 취소와 오류에서도 운영체제 입력 차단을 복구한다.</summary>
    private static string ChooseImageInternal()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel("Choose keyboard photo", "", "png,jpg,jpeg");
#elif UNITY_STANDALONE_WIN
        WindowsSpecialKeyInput.SuspendForFileDialog();
        IntPtr file = IntPtr.Zero;
        IntPtr filter = IntPtr.Zero;
        IntPtr title = IntPtr.Zero;
        try
        {
            file = Marshal.AllocHGlobal(FILE_PATH_CAPACITY * sizeof(char));
            Marshal.WriteInt16(file, 0);
            filter = Marshal.StringToHGlobalUni("Images (PNG, JPG, JPEG)\0*.png;*.jpg;*.jpeg\0\0");
            title = Marshal.StringToHGlobalUni("키보드에 적용할 이미지 선택");
            var settings = new OpenFileName(file, filter, title);
            if (GetOpenFileName(ref settings)) return Marshal.PtrToStringUni(file);
            uint error = CommDlgExtendedError();
            if (error != 0) throw new InvalidOperationException("파일 선택 창 오류: 0x" + error.ToString("X"));
            return string.Empty;
        }
        finally
        {
            if (file != IntPtr.Zero) Marshal.FreeHGlobal(file);
            if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
            if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
            WindowsSpecialKeyInput.ResumeAfterFileDialog();
        }
#else
        throw new PlatformNotSupportedException("이 플랫폼에서는 이미지 파일 경로를 직접 입력해 주세요.");
#endif
    }
}
