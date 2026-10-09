namespace OpenClick.App.Services;

/// <summary>English + Arabic strings with RTL support. No external dependencies.</summary>
public sealed class LocalizationService
{
    private string _lang = "en";
    public string Language => _lang;
    public event Action? LanguageChanged;

    private static readonly Dictionary<string, (string En, string Ar)> Strings = new()
    {
        ["AppTitle"] = ("Open Click", "أوبن كليك"),
        ["Dashboard"] = ("Dashboard", "لوحة التحكم"),
        ["ClickFilter"] = ("Click Filter", "مرشح النقرات"),
        ["MouseTester"] = ("Mouse Tester", "اختبار الفأرة"),
        ["Profiles"] = ("Profiles", "الملفات"),
        ["Settings"] = ("Settings", "الإعدادات"),
        ["EnableFilter"] = ("Enable Filter", "تفعيل المرشح"),
        ["DisableFilter"] = ("Disable Filter", "تعطيل المرشح"),
        ["FilterEnabled"] = ("Filter enabled", "المرشح مفعّل"),
        ["FilterDisabled"] = ("Filter disabled", "المرشح معطّل"),
        ["FilterError"] = ("Filter error", "خطأ في المرشح"),
        ["Interval"] = ("Interval", "الفاصل الزمني"),
        ["Mode"] = ("Mode", "الوضع"),
        ["Observed"] = ("Observed", "المرصودة"),
        ["Suppressed"] = ("Suppressed", "المحجوبة"),
        ["Allowed"] = ("Allowed", "المسموحة"),
        ["ResetStats"] = ("Reset statistics", "تصفير الإحصاءات"),
        ["ActiveProfile"] = ("Active profile", "الملف النشط"),
        ["OpenTester"] = ("Open Tester", "فتح الاختبار"),
        ["OpenSettings"] = ("Open Settings", "فتح الإعدادات"),
        ["StartTest"] = ("Start session", "بدء الجلسة"),
        ["StopTest"] = ("Stop session", "إنهاء الجلسة"),
        ["ClearEvents"] = ("Clear events", "مسح الأحداث"),
        ["CopySummary"] = ("Copy summary", "نسخ الملخص"),
        ["Ms"] = ("ms", "م.ث"),
        ["LeftButton"] = ("Left", "الأيسر"),
        ["RightButton"] = ("Right", "الأيمن"),
        ["MiddleButton"] = ("Middle", "الأوسط"),
        ["BypassActive"] = ("Bypass active", "التجاوز نشط"),
        ["TrayOpen"] = ("Open Open Click", "فتح أوبن كليك"),
        ["TrayToggle"] = ("Toggle Filter", "تبديل المرشح"),
        ["TrayTester"] = ("Open Mouse Tester", "فتح اختبار الفأرة"),
        ["TraySettings"] = ("Settings", "الإعدادات"),
        ["TrayExit"] = ("Exit", "خروج"),
        ["Appearance"] = ("Appearance", "المظهر"),
        ["DarkTheme"] = ("Dark", "داكن"),
        ["LightTheme"] = ("Light", "فاتح"),
        ["Language"] = ("Language", "اللغة"),
        ["Save"] = ("Save", "حفظ"),
        ["Cancel"] = ("Cancel", "إلغاء"),
        ["Create"] = ("Create", "إنشاء"),
        ["Rename"] = ("Rename", "إعادة تسمية"),
        ["Duplicate"] = ("Duplicate", "تكرار"),
        ["Delete"] = ("Delete", "حذف"),
        ["Import"] = ("Import", "استيراد"),
        ["Export"] = ("Export", "تصدير"),
        ["SetDefault"] = ("Set default", "تعيين افتراضي"),
        ["FilterOff"] = ("Filter Off", "المرشح متوقف"),
        ["WarningLongInterval"] = ("Longer intervals may also suppress intentional rapid clicks.", "الفواصل الأطول قد تحجب النقرات السريعة المقصودة أيضًا."),
        ["DragNote"] = ("Dragging and holding buttons always pass through.", "السحب والضغط المستمر يمران دائمًا."),
        ["HardwareNote"] = ("A software filter cannot repair a defective switch. Rapid patterns can come from switch bounce, a failing switch, fast clicking, or software-generated events.", "لا يمكن لمرشح برمجي إصلاح مفتاح تالف. قد تنتج الأنماط السريعة عن اهتزاز المفتاح أو تلفه أو النقر السريع أو أحداث برمجية."),
        ["UipiNote"] = ("Some elevated / protected windows, remote sessions, and games may not be affected due to Windows security boundaries (UIPI).", "قد لا تتأثر بعض النوافذ المرتفعة أو المحمية وجلسات سطح المكتب البعيد والألعاب بسبب حدود أمان ويندوز."),
    };

    public void SetLanguage(string lang)
    {
        if (lang != "en" && lang != "ar") lang = "en";
        if (_lang == lang) return;
        _lang = lang;
        LanguageChanged?.Invoke();
    }

    public string T(string key)
    {
        if (Strings.TryGetValue(key, out var v))
            return _lang == "ar" ? v.Ar : v.En;
        return key;
    }

    public bool IsRtl => _lang == "ar";

    /// <summary>Preferred font chain: خط ثمانية (Thamaniya) when available, else Arabic-capable fallbacks.</summary>
    public string FontFamilyName => _lang == "ar"
        ? "Thamaniya, IBM Plex Sans Arabic, Cairo, Tajawal, Segoe UI"
        : "Segoe UI";
}
