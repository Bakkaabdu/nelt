using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Infrastructure.Persistence;

/// <summary>
/// Reference data created once on an empty database: the standard CEFR (German) and HSK (Chinese) proficiency levels,
/// and starter copy for the public site. Everything here is editable in the admin panel afterwards.
/// </summary>
internal static class SeedContent
{
    public static PlatformSettings Settings() => new()
    {
        Id = PlatformSettings.SingletonId,
        SiteName = "Nelt",
        Tagline = LocalizedText.Of(
            "German & Chinese language courses",
            "دورات اللغتين الألمانية والصينية",
            "Deutsch- und Chinesischkurse",
            "德语与中文课程"),
        HeroTitle = LocalizedText.Of(
            "Learn German and Chinese, from the first word to fluency.",
            "تعلّم الألمانية والصينية، من الكلمة الأولى حتى الطلاقة.",
            "Deutsch und Chinesisch lernen – vom ersten Wort bis zur Sicherheit.",
            "学习德语和中文，从第一个词到流利表达。"),
        HeroSubtitle = LocalizedText.Of(
            "Structured courses for every level, in class and online — with video lessons, level-based materials, quizzes and progress you can see.",
            "دورات منظمة لكل المستويات، حضوريًا وعبر الإنترنت — مع دروس مرئية ومواد حسب المستوى واختبارات وتقدّم واضح.",
            "Strukturierte Kurse für jedes Niveau, vor Ort und online – mit Videolektionen, Materialien pro Niveau, Tests und sichtbarem Fortschritt.",
            "覆盖所有级别的系统课程，线下与线上同步——配有视频课程、分级学习资料、测验以及清晰可见的学习进度。"),
        AboutTitle = LocalizedText.Of("About Nelt", "عن نِلت", "Über Nelt", "关于 Nelt"),
        AboutBody = LocalizedText.Of(
            "Nelt is a language school for German and Chinese. Every course follows an internationally recognised level framework, combines live teaching with guided self-study, and ends with a final exam and a certificate.",
            "نِلت مدرسة لتعليم اللغتين الألمانية والصينية. تتبع كل دورة إطارًا دوليًا معتمدًا للمستويات، وتجمع بين التدريس المباشر والتعلّم الذاتي الموجّه، وتنتهي باختبار نهائي وشهادة.",
            "Nelt ist eine Sprachschule für Deutsch und Chinesisch. Jeder Kurs folgt einem international anerkannten Niveaurahmen, verbindet Präsenzunterricht mit begleitetem Selbststudium und endet mit einer Abschlussprüfung und einem Zertifikat.",
            "Nelt 是一所德语和中文语言学校。每门课程都遵循国际公认的等级体系，将课堂教学与有指导的自主学习相结合，并以结业考试和证书收尾。"),
        PaymentInstructions = LocalizedText.Of(
            "Your seat is reserved as soon as you enroll. Pay at the front desk or by bank transfer — your access is activated once the payment is confirmed.",
            "يُحجز مقعدك فور التسجيل. ادفع في مكتب الاستقبال أو عبر تحويل مصرفي — ويُفعَّل وصولك بمجرد تأكيد الدفع.",
            "Ihr Platz ist reserviert, sobald Sie sich anmelden. Bezahlen Sie am Empfang oder per Überweisung – Ihr Zugang wird nach Zahlungseingang freigeschaltet.",
            "报名后即为您保留名额。请在前台或通过银行转账付款——确认收款后即开通学习权限。"),
    };

    public static IEnumerable<Level> Levels()
    {
        (string Code, string En, string Ar, string De, string Zh)[] cefr =
        [
            ("A1", "Beginner", "مبتدئ", "Anfänger", "入门级"),
            ("A2", "Elementary", "أساسي", "Grundstufe", "初级"),
            ("B1", "Intermediate", "متوسط", "Mittelstufe", "中级"),
            ("B2", "Upper intermediate", "فوق المتوسط", "Gehobene Mittelstufe", "中高级"),
            ("C1", "Advanced", "متقدم", "Fortgeschritten", "高级"),
            ("C2", "Mastery", "إتقان", "Kompetente Sprachverwendung", "精通级"),
        ];

        (string Code, string En, string Ar, string De, string Zh)[] hsk =
        [
            ("HSK1", "Beginner", "مبتدئ", "Anfänger", "一级"),
            ("HSK2", "Elementary", "أساسي", "Grundstufe", "二级"),
            ("HSK3", "Pre-intermediate", "ما قبل المتوسط", "Untere Mittelstufe", "三级"),
            ("HSK4", "Intermediate", "متوسط", "Mittelstufe", "四级"),
            ("HSK5", "Upper intermediate", "فوق المتوسط", "Gehobene Mittelstufe", "五级"),
            ("HSK6", "Advanced", "متقدم", "Fortgeschritten", "六级"),
            ("HSK7-9", "Mastery", "إتقان", "Meisterstufe", "七至九级"),
        ];

        return cefr.Select((l, i) => Create(TargetLanguage.German, l, i + 1))
            .Concat(hsk.Select((l, i) => Create(TargetLanguage.Chinese, l, i + 1)));

        static Level Create(TargetLanguage language, (string Code, string En, string Ar, string De, string Zh) l, int rank) => new()
        {
            Language = language,
            Code = l.Code,
            Rank = rank,
            Name = LocalizedText.Of(l.En, l.Ar, l.De, l.Zh),
        };
    }
}
