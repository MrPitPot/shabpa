namespace Shabpa.Core;

/// <summary>A one-line verdict on how much the running game weighs on the machine, from the live numbers.</summary>
public static class GameImpact
{
    /// <summary>0 = fine, 1 = busy, 2 = under strain.</summary>
    public static int Level(double cpu, double? gpu, double ram, double? cpuTemp, double? gpuTemp)
    {
        if ((gpuTemp ?? 0) >= 85 || (cpuTemp ?? 0) >= 90 || ram >= 92) return 2;
        if ((gpu ?? 0) >= 90 || cpu >= 90 || ram >= 80 || (gpuTemp ?? 0) >= 78 || (cpuTemp ?? 0) >= 80) return 1;
        return 0;
    }

    public static string Describe(double cpu, double? gpu, double ram, double? cpuTemp, double? gpuTemp)
    {
        if ((gpuTemp ?? 0) >= 85) return L.T("کارت گرافیک خیلی داغه؛ تهویه و فن‌ها رو ببین.", "The graphics card is very hot; check airflow and fans.");
        if ((cpuTemp ?? 0) >= 90) return L.T("پردازنده خیلی داغه؛ خنک‌کننده رو چک کن.", "The processor is very hot; check the cooler.");
        if (ram >= 92) return L.T("رم تقریباً پره؛ برنامه‌های کناری رو ببند.", "Memory is almost full; close other programs.");
        if ((gpu ?? 0) >= 90 && cpu < 70) return L.T("گرافیک زیر فشار کامله؛ پردازنده راحته.", "The graphics card is maxed out; the processor has room.");
        if (cpu >= 90) return L.T("پردازنده زیر فشار کامله.", "The processor is maxed out.");
        if (ram >= 80) return L.T("رم پرمصرفه ولی هنوز جا داره.", "Memory use is high but there is still room.");
        if ((gpuTemp ?? 0) >= 78 || (cpuTemp ?? 0) >= 80) return L.T("دما کمی بالاست ولی در محدودهٔ عادی بازیه.", "Temperatures are a little high but normal for gaming.");
        return L.T("سیستم راحته؛ بازی فشار خاصی نمیاره.", "The system is comfortable; the game is not straining it.");
    }
}
