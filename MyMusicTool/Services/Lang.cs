using System;
using System.Linq;
using System.Windows;

namespace MyMusicTool.Services
{
    public static class Lang
    {
        public static event Action? LanguageChanged;

        public static string CurrentLanguage { get; private set; } = "zh-CN";

      
        //获取当前语言词条
        public static string Get(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

     
        // 获取带格式化参数的当前语言词条（如 "播放队列 ({0})"）
        public static string Format(string key, params object[] args)
            => string.Format(Get(key), args);

     
        // 实时切换界面语言（"zh-CN" 或 "en-US"）
        public static void SwitchLanguage(string cultureCode)
        {
            if (CurrentLanguage == cultureCode) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Languages/{cultureCode}.xaml", UriKind.Absolute)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            var oldLang = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Languages/") == true);
            if (oldLang != null)
            {
                merged.Remove(oldLang);
            }
            merged.Add(dict);

            CurrentLanguage = cultureCode;
            LanguageChanged?.Invoke();
        }
    }
}