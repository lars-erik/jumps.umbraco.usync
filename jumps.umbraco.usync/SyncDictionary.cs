﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using umbraco.cms.businesslogic;
using System.Xml;
using System.IO;
using Umbraco.Core.IO; 
using Umbraco.Core.Logging ; 
using umbraco.BusinessLogic;


namespace jumps.umbraco.usync
{
    public class SyncDictionary
    {
        public static void SaveToDisk(Dictionary.DictionaryItem item)
        {
            if (item != null)
            {
                XmlDocument xmlDoc = helpers.XmlDoc.CreateDoc();
                xmlDoc.AppendChild(item.ToXml(xmlDoc));
                helpers.XmlDoc.SaveXmlDoc("Dictionary", item.key, xmlDoc);
            }
        }

        public static void SaveAllToDisk()
        {
            LogHelper.Debug<uSync>("Saving Dictionary Types");

            foreach (Dictionary.DictionaryItem item in Dictionary.getTopMostItems)
            {
                LogHelper.Debug<uSync>("Dictionary Item {0}", ()=> item.key);
                SaveToDisk(item);
            }
        }

        public static void ReadAllFromDisk()
        {
            string path = IOHelper.MapPath(string.Format("{0}{1}",
                helpers.uSyncIO.RootFolder,
                "Dictionary"));

            ReadFromDisk(path);

        }

        public static void ReadFromDisk(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.GetFiles(path, "*.config"))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(file);

                    XmlNode node = xmlDoc.SelectSingleNode("./DictionaryItem");

                    if (node != null)
                    {
                        LogHelper.Debug<uSync>("Node Import: {0} {1}", ()=> node.Attributes["Key"].Value, ()=> node.InnerXml);

                        try
                        {

                            Dictionary.DictionaryItem item = Dictionary.DictionaryItem.Import(node);

                            if (item != null)
                                item.Save();
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error<uSync>("DictionaryItem.Import Failed {0}", ex);
                        }
                    }
                }
            }
           
        }

        public static void AttachEvents()
        {
            Dictionary.DictionaryItem.Saving += DictionaryItem_Saving;
            Dictionary.DictionaryItem.Deleting += DictionaryItem_Deleting;
        }

        static object _deleteLock = new object();
        static System.Collections.ArrayList _dChildren = new System.Collections.ArrayList(); 

        static void DictionaryItem_Deleting(Dictionary.DictionaryItem sender, EventArgs e)
        {
            lock (_deleteLock)
            {
                if (sender.hasChildren)
                {
                    // we get the delets in a backwards order, so we add all the children of this
                    // node to the list we are not going to delete when we get asked to.
                    // 
                    foreach(Dictionary.DictionaryItem child in sender.Children)
                    {
                        _dChildren.Add(child.id) ; 
                    }
                }

                if (_dChildren.Contains(sender.id))
                {
                    // this is a child of a parent we have already deleted.
                    _dChildren.Remove(sender.id);
                    LogHelper.Debug<uSync>("No Deleteing Dictionary item {0} because we deleted it's parent", ()=> sender.key); 
                }
                else
                {
                    //actually delete 


                    LogHelper.Debug<uSync>("Deleting Dictionary Item {0}", ()=> sender.key);

                    // when you delete a tree, the top gets called before the children. 
                    //             
                    if (!sender.IsTopMostItem())
                    {
                        // if it's not top most, we save it's parent (that will delete)

                        SaveToDisk(GetTop(sender));
                    }
                    else
                    {
                        // it's top we need to delete
                        helpers.XmlDoc.ArchiveFile("Dictionary", sender.key);

                    }
                }
            }
            
            
            

        }


        static void DictionaryItem_Saving(Dictionary.DictionaryItem sender, EventArgs e)
        {
            SaveToDisk(GetTop(sender));
        }

        private static Dictionary.DictionaryItem GetTop(Dictionary.DictionaryItem item)
        {
            if (!item.IsTopMostItem())
            {
                LogHelper.Debug<uSync>("is Top Most [{0}]", ()=> item.IsTopMostItem());
                try
                {
                    if (item.Parent != null)
                    {
                        LogHelper.Debug<uSync>("parent [{0}]", () => item.Parent.key);
                        return GetTop(item.Parent);
                    }
                }
                catch (ArgumentException aex)
                {
                    LogHelper.Info<uSync>("Argument Exception - basically Parent is Null");
                }
            }

            return item; 

        }
    }
}
