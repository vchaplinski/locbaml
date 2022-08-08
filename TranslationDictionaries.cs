//---------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// Description: TranslationDictionariesWriter & TranslationDictionariesReader class
//
//---------------------------------------------------------------------------

using System;
using System.IO;
using System.Resources;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Markup.Localizer;
using System.Collections.Generic;
using System.Linq;

namespace BamlLocalization
{
    internal class Resource
    {
        internal string Baml { get; set; }
        internal string Key { get; set; }
        internal string Category { get; set; }
        internal bool Readable { get; set; }
        internal bool Modifiable { get; set; }
        internal string Comments { get; set; }
        internal string Content { get; set; }
        internal string OriginalContent { get; set; }
        internal UpdateTag? UpdateTag { get; set; }
    }

    /// <summary>
    /// Writer to write out localizable values into CSV or tab-separated txt files.     
    /// </summary>
    internal static class  TranslationDictionariesWriter
    {
        /// <summary>
        /// Write the localizable key-value pairs
        /// </summary>
        /// <param name="options"></param>
        internal static void Write(LocBamlOptions options)            
        {   
            // If update is requested and output file exists - load it for update
            List<Resource> translatedResources = new List<Resource>();
            if (options.Update && File.Exists(options.Output))
            {
                using (Stream input = File.OpenRead(options.Output))
                {
                    using (ResourceTextReader reader = new ResourceTextReader(options.TranslationFileType, input))
                    {
                        translatedResources.AddRange(GetResources(reader));
                    }
                }
            }

            Stream output = new FileStream(options.Output, FileMode.Create);
            InputBamlStreamList bamlStreamList = new InputBamlStreamList(options);

            using (ResourceTextWriter writer = new ResourceTextWriter(options.TranslationFileType, output))
            {
                options.WriteLine(StringLoader.Get("WriteBamlValues"));
                for (int i = 0; i < bamlStreamList.Count; i++)
                {
                    options.Write("    ");
                    options.Write(StringLoader.Get("ProcessingBaml", bamlStreamList[i].Name));

                    // Search for comment file in the same directory. The comment file has the extension to be 
                    // "loc".
                    string commentFile = Path.ChangeExtension(bamlStreamList[i].Name, "loc");
                    TextReader commentStream = null;

                    try
                    {
                        if (File.Exists(commentFile))
                        {
                            commentStream = new StreamReader(commentFile);
                        }

                        // create the baml localizer
                        BamlLocalizer mgr = new BamlLocalizer(
                            bamlStreamList[i].Stream,
                            new BamlLocalizabilityByReflection(options.Assemblies),
                            commentStream
                            );

                        // extract localizable resource from the baml stream
                        BamlLocalizationDictionary dict = mgr.ExtractResources();

                        // write out each resource
                        foreach (DictionaryEntry entry in dict)
                        {
                            // column 1: baml stream name
                            writer.WriteColumn(bamlStreamList[i].Name);
                            
                            BamlLocalizableResourceKey key = (BamlLocalizableResourceKey) entry.Key;
                            BamlLocalizableResource resource = (BamlLocalizableResource)entry.Value;

                            // column 2: localizable resource key
                            writer.WriteColumn(LocBamlConst.ResourceKeyToString(key));

                            // column 3: localizable resource's category
                            writer.WriteColumn(resource.Category.ToString());

                            // column 4: localizable resource's readability
                            writer.WriteColumn(resource.Readable.ToString());

                            // column 5: localizable resource's modifiability
                            writer.WriteColumn(resource.Modifiable.ToString());

                            // column 6: localizable resource's localization comments
                            writer.WriteColumn(resource.Comments);

                            // update the content if previosly available
                            // Note: columns 8 and 9 are not used for baml generation, they are only to support Update mode
                            Resource translatedResource = translatedResources.FirstOrDefault(r => r.Baml == bamlStreamList[i].Name && r.Key == LocBamlConst.ResourceKeyToString(key));

                            // column 7: already translated content
                            writer.WriteColumn(translatedResource == null ? resource.Content : translatedResource.Content);

                            // column 8: current original content - save for reference 
                            writer.WriteColumn(resource.Content);

                            // column 9: update tag
                            UpdateTag? updateTag = (translatedResource == null) ? UpdateTag.New : 
                                (resource.Content == translatedResource.OriginalContent) ? translatedResource.UpdateTag : UpdateTag.Changed;
                            writer.WriteColumn(updateTag.HasValue ? updateTag.ToString() : string.Empty);

                            // Done. finishing the line
                            writer.EndLine();
                        }

                        options.WriteLine(StringLoader.Get("Done"));
                    }
                    finally
                    {
                        if (commentStream != null)
                            commentStream.Close();
                    }
                }
                
                // close all the baml input streams, output stream is closed by writer.
                bamlStreamList.Close();            
            }   
        }

        /// <summary>
        /// Check if all localizable resources are translated
        /// </summary>
        /// <param name="options"></param>
        internal static bool Check(LocBamlOptions options)
        {
            int failCount = 0;

            if (!File.Exists(options.Output)) return false;

            List<Resource> translatedResources = new List<Resource>();
            using (Stream input = File.OpenRead(options.Output))
            {
                using (ResourceTextReader reader = new ResourceTextReader(options.TranslationFileType, input))
                {
                    translatedResources.AddRange(GetResources(reader));
                }
            }

            InputBamlStreamList bamlStreamList = new InputBamlStreamList(options);

            for (int i = 0; i < bamlStreamList.Count; i++)
            {
                options.Write("    ");
                options.WriteLine(StringLoader.Get("ProcessingBaml", bamlStreamList[i].Name));

                // create the baml localizer
                BamlLocalizer mgr = new BamlLocalizer(
                    bamlStreamList[i].Stream,
                    new BamlLocalizabilityByReflection(options.Assemblies),
                    null
                    );

                // extract localizable resource from the baml stream
                BamlLocalizationDictionary dict = mgr.ExtractResources();

                // check each resource
                foreach (DictionaryEntry entry in dict)
                {
                    BamlLocalizableResourceKey key = (BamlLocalizableResourceKey)entry.Key;
                    BamlLocalizableResource resource = (BamlLocalizableResource)entry.Value;

                    Resource translatedResource = translatedResources.FirstOrDefault(r => r.Baml == bamlStreamList[i].Name && r.Key == LocBamlConst.ResourceKeyToString(key));

                    if ((translatedResource == null) || (resource.Content != translatedResource.OriginalContent))
                    {
                        Console.WriteLine(StringLoader.Get("OutOfSync"));
                        failCount++;
                        break;
                    }
                    else if (translatedResource.UpdateTag.HasValue)
                    {
                        Console.WriteLine(StringLoader.Get("ResourceTagged", translatedResource.UpdateTag, bamlStreamList[i].Name, LocBamlConst.ResourceKeyToString(key)));
                        failCount++;
                    }
                }

                options.WriteLine(StringLoader.Get("Done"));
            }

            // close all the baml input streams, output stream is closed by writer.
            bamlStreamList.Close();

            if (failCount > 0)
            {
                Console.WriteLine(StringLoader.Get("CheckFailed", failCount));
            }
            else
            {
                Console.WriteLine(StringLoader.Get("CheckSuccess", failCount));
            }

            return failCount == 0;
        }

        private static IList<Resource> GetResources(ResourceTextReader reader)
        {
            List<Resource> list = new List<Resource>();

            if (reader == null)
                throw new ArgumentNullException("reader");

            // we read each Row
            int rowNumber = 0;
            while (reader.ReadRow())
            {
                rowNumber++;

                // field #1 is the baml name.
                string bamlName = reader.GetColumn(0);

                // it can't be null
                if (bamlName == null)
                    throw new ApplicationException(StringLoader.Get("EmptyRowEncountered"));

                if (string.IsNullOrEmpty(bamlName))
                {
                    // allow for comment lines in csv file.
                    // each comment line starts with ",". It will make the first entry as String.Empty.
                    // and we will skip the whole line.
                    continue;   // if the first column is empty, take it as a comment line
                }

                // field #2: key to the localizable resource
                string key = reader.GetColumn(1);
                if (key == null)
                    throw new ApplicationException(StringLoader.Get("NullBamlKeyNameInRow"));

                BamlLocalizableResourceKey resourceKey = LocBamlConst.StringToResourceKey(key);

                Resource resource;

                // the rest of the fields are either all null,
                // or all non-null. If all null, it means the resource entry is deleted.

                // get the string category
                string categoryString = reader.GetColumn(2);
                if (categoryString == null)
                {
                    // it means all the following fields are null starting from column #3.
                    resource = null;
                }
                else
                {
                    // the rest must all be non-null.
                    // the last cell can be null if there is no content
                    for (int i = 3; i < 6; i++)
                    {
                        if (reader.GetColumn(i) == null)
                            throw new Exception(StringLoader.Get("InvalidRow"));
                    }

                    // now we know all are non-null. let's try to create a resource
                    resource = new Resource();

                    resource.Baml = bamlName;

                    resource.Key = key;

                    // field #3: Category
                    resource.Category = categoryString;

                    // field #4: Readable
                    resource.Readable = (bool)BoolTypeConverter.ConvertFrom(reader.GetColumn(3));

                    // field #5: Modifiable
                    resource.Modifiable = (bool)BoolTypeConverter.ConvertFrom(reader.GetColumn(4));

                    // field #6: Comments
                    resource.Comments = reader.GetColumn(5);

                    // field #7: Content
                    resource.Content = reader.GetColumn(6) ?? string.Empty;

                    // field #8: Original content
                    resource.OriginalContent = reader.GetColumn(7) ?? string.Empty;

                    // field #9: UpdateTag
                    string updateTagStr = reader.GetColumn(8);
                    resource.UpdateTag = string.IsNullOrEmpty(updateTagStr) ? (UpdateTag?)null : (UpdateTag)UpdateTagConverter.ConvertFrom(updateTagStr);

                    // field > #9: Ignored.
                }

                list.Add(resource);
            }

            return list;
        }

        private static TypeConverter BoolTypeConverter = TypeDescriptor.GetConverter(true);
        private static TypeConverter UpdateTagConverter = TypeDescriptor.GetConverter(UpdateTag.Changed);
    }

    // NEW
    internal enum UpdateTag
    {
        New,
        Changed
    }

    /// <summary>
    /// Reader to read the translations from CSV or tab-separated txt file    
    /// </summary> 
    internal class TranslationDictionariesReader
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="reader">resoure text reader that reads CSV or a tab-separated txt file</param>
        internal TranslationDictionariesReader(ResourceTextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");

            // hash key is case insensitive strings
            _table = new Hashtable();

            // we read each Row
            int rowNumber = 0;
            while (reader.ReadRow())
            {
                rowNumber ++;
                
                // field #1 is the baml name.
                string bamlName = reader.GetColumn(0);

                // it can't be null
                if (bamlName == null)
                    throw new ApplicationException(StringLoader.Get("EmptyRowEncountered"));

                if (string.IsNullOrEmpty(bamlName))
                {
                    // allow for comment lines in csv file.
                    // each comment line starts with ",". It will make the first entry as String.Empty.
                    // and we will skip the whole line.
                    continue;   // if the first column is empty, take it as a comment line
                }

                // field #2: key to the localizable resource
                string key = reader.GetColumn(1);
                if (key == null)
                    throw new ApplicationException(StringLoader.Get("NullBamlKeyNameInRow"));

                BamlLocalizableResourceKey resourceKey = LocBamlConst.StringToResourceKey(key);

                // get the dictionary 
                BamlLocalizationDictionary dictionary = this[bamlName];                
                if (dictionary == null)
                {   
                    // we create one if it is not there yet.
                    dictionary = new BamlLocalizationDictionary();
                    this[bamlName] = dictionary;                
                }
                
                BamlLocalizableResource resource;
                
                // the rest of the fields are either all null,
                // or all non-null. If all null, it means the resource entry is deleted.
                
                // get the string category
                string categoryString = reader.GetColumn(2);
                if (categoryString == null)
                {
                    // it means all the following fields are null starting from column #3.
                    resource = null;
                }
                else
                {
                    // the rest must all be non-null.
                    // the last cell can be null if there is no content
                    for (int i = 3; i < 6; i++)
                    {
                        if (reader.GetColumn(i) == null)
                            throw new Exception(StringLoader.Get("InvalidRow"));
                    }

                    // now we know all are non-null. let's try to create a resource
                    resource  = new BamlLocalizableResource();
                    
                    // field #3: Category
                    resource.Category = (LocalizationCategory) StringCatConverter.ConvertFrom(categoryString);
                    
                    // field #4: Readable
                    resource.Readable     = (bool) BoolTypeConverter.ConvertFrom(reader.GetColumn(3));

                    // field #5: Modifiable
                    resource.Modifiable   = (bool) BoolTypeConverter.ConvertFrom(reader.GetColumn(4));

                    // field #6: Comments
                    resource.Comments     = reader.GetColumn(5);

                    // field #7: Content
                    resource.Content      = reader.GetColumn(6);

                    // in case content being the last column, consider null as empty.
                    if (resource.Content == null)
                        resource.Content = string.Empty;

                    // field > #7: Ignored.
                }                             

                // at this point, we are good.
                // add to the dictionary.
                dictionary.Add(resourceKey, resource);
            }        
        }

        internal BamlLocalizationDictionary this[string key]
        {
            get{
                return (BamlLocalizationDictionary) _table[key.ToLowerInvariant()];
            }
            set
            {
                _table[key.ToLowerInvariant()] = value;
            }
        }

        // hashtable that maps from baml name to its ResourceDictionary
        private Hashtable _table;                
        private static TypeConverter BoolTypeConverter  = TypeDescriptor.GetConverter(true);
        private static TypeConverter StringCatConverter = TypeDescriptor.GetConverter(LocalizationCategory.Text);
    }
}
