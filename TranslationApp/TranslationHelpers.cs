﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceManager;
using ResourceManager.Xliff;
using TranslationUtl = ResourceManager.Xliff.TranslationUtl;

namespace TranslationApp
{
    internal static class TranslationHelpers
    {
        public static IDictionary<string, List<TranslationItemWithCategory>> LoadNeutralItems()
        {
            IDictionary<string, TranslationFile> neutralTranslation = new Dictionary<string, TranslationFile>();
            try
            {
                // Set language to neutral to get neutral translations
                GitCommands.AppSettings.CurrentTranslation = "";

                var translatableTypes = TranslationUtl.GetTranslatableTypes();
                foreach (var types in translatableTypes)
                {
                    var translation = new TranslationFile();
                    try
                    {
                        foreach (Type type in types.Value)
                        {
                            if (TranslationUtl.CreateInstanceOfClass(type) is ITranslate obj)
                            {
                                obj.AddTranslationItems(translation);
                                if (obj is IDisposable disposable)
                                {
                                    disposable.Dispose();
                                }
                            }
                        }
                    }
                    finally
                    {
                        translation.Sort();
                        neutralTranslation[types.Key] = translation;
                    }
                }
            }
            finally
            {
                // Restore translation
                GitCommands.AppSettings.CurrentTranslation = null;
            }

            return GetItemsDictionary(neutralTranslation);
        }

        public static IDictionary<string, List<TranslationItemWithCategory>> GetItemsDictionary(IDictionary<string, TranslationFile> translations)
        {
            var items = new Dictionary<string, List<TranslationItemWithCategory>>();
            foreach (var pair in translations)
            {
                var list = from item in pair.Value.TranslationCategories
                           from translationItem in item.Body.TranslationItems
                           select new TranslationItemWithCategory(item.Name, translationItem);
                items.Add(pair.Key, list.ToList());
            }

            return items;
        }

        private static List<T> Find<T>(this IDictionary<string, List<T>> dictionary, string key)
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                list = new List<T>();
                dictionary.Add(key, list);
            }

            return list;
        }

        public static IDictionary<string, List<TranslationItemWithCategory>> LoadTranslation(
            IDictionary<string, TranslationFile> translation, IDictionary<string, List<TranslationItemWithCategory>> neutralItems)
        {
            var translateItems = new Dictionary<string, List<TranslationItemWithCategory>>();

            var oldTranslationItems = GetItemsDictionary(translation);

            foreach (var pair in neutralItems)
            {
                var oldItems = oldTranslationItems.Find(pair.Key);
                var transItems = translateItems.Find(pair.Key);
                var dict = new Dictionary<string, string>();
                foreach (var item in pair.Value)
                {
                    var curItems = oldItems.Where(
                        oldItem => oldItem.Category.TrimStart('_') == item.Category.TrimStart('_') &&
                                  oldItem.Name.TrimStart('_') == item.Name.TrimStart('_') &&
                                  oldItem.Property == item.Property);
                    var curItem = curItems.FirstOrDefault();

                    if (curItem == null)
                    {
                        curItem = item.Clone();
                        transItems.Add(curItem);
                        continue;
                    }

                    oldItems.Remove(curItem);
                    curItem.Category = item.Category;
                    curItem.Name = item.Name;

                    string source = curItem.NeutralValue ?? item.NeutralValue;
                    if (!string.IsNullOrEmpty(curItem.TranslatedValue) && !dict.ContainsKey(source))
                    {
                        dict.Add(source, curItem.TranslatedValue);
                    }

                    // Source text changed
                    if (!string.IsNullOrEmpty(curItem.TranslatedValue) && !curItem.IsSourceEqual(item.NeutralValue))
                    {
                        curItem.TranslatedValue = "";
                    }

                    curItem.NeutralValue = item.NeutralValue;
                    transItems.Add(curItem);
                }

                foreach (var item in oldItems)
                {
                    // Obsolete should be added only to dictionary
                    if (!string.IsNullOrEmpty(item.TranslatedValue) &&
                        item.NeutralValue != null && !dict.ContainsKey(item.NeutralValue))
                    {
                        dict.Add(item.NeutralValue, item.TranslatedValue);
                    }
                }

                // update untranslated items
                var untranlatedItems =
                    from transItem in transItems
                    where string.IsNullOrEmpty(transItem.TranslatedValue) && dict.ContainsKey(transItem.NeutralValue)
                    select transItem;

                foreach (var untranlatedItem in untranlatedItems)
                {
                    untranlatedItem.TranslatedValue = dict[untranlatedItem.NeutralValue];
                }
            }

            return translateItems;
        }

        public static void SaveTranslation(string targetLanguageCode,
            IDictionary<string, List<TranslationItemWithCategory>> items, string filename)
        {
            var ext = Path.GetExtension(filename);
            foreach (var pair in items)
            {
                var foreignTranslation = new TranslationFile(GitCommands.AppSettings.ProductVersion, "en", targetLanguageCode);
                foreach (var translateItem in pair.Value)
                {
                    var item = translateItem.GetTranslationItem();

                    var ti = new TranslationItem(item.Name, item.Property, item.Source, item.Value);
                    ti.Value = ti.Value ?? string.Empty;
                    foreignTranslation.FindOrAddTranslationCategory(translateItem.Category)
                        .Body.AddTranslationItem(ti);
                }

                var newfilename = Path.ChangeExtension(filename, pair.Key + ext);
                TranslationSerializer.Serialize(foreignTranslation, newfilename);
            }
        }
    }
}
