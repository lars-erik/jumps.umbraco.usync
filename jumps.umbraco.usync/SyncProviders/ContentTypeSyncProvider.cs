﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using System.Xml.Linq ; 

using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

using Umbraco.Core.Logging;

namespace jumps.umbraco.usync.SyncProviders
{
    /// <summary>
    ///  does stuff to export/import ContentTypes 
    ///  (DocumentTypes)
    /// </summary>
    public static class ContentTypeSyncProvider
    {
        static PackagingService _packService;
        static IContentTypeService _contentTypeService; 

        static ContentTypeSyncProvider()
        {
            _packService = ApplicationContext.Current.Services.PackagingService;
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
        }

        public static XElement SyncExport(this IContentType item)
        {
            XElement element = _packService.Export(item);

            element.Element("Info").Add(new XElement("key", item.Key)); 

            // fix, current api - doesn't do structure proper 
            var structure = element.Element("Structure");
            foreach (var allowedType in item.AllowedContentTypes)
            {
                var allowedItem = _contentTypeService.GetContentType(allowedType.Id.Value);

                // do it like this, so if the core is fixed we will just skip on
                if (!structure.Elements().Any(x => x.Value == allowedItem.Alias))
                {
                    structure.Add(new XElement("DocumentType", allowedItem.Alias));
                }
                allowedItem.DisposeIfDisposable();
            }

            var tabs = element.Element("Tabs");
            foreach (var tab in item.PropertyGroups)
            {
                XElement tabNode = tabs.Elements().First(x => x.Element("Id").Value == tab.Id.ToString() ) ;

                if ( tabNode != null ) 
                {
                    tabNode.Add(new XElement("SortOrder", tab.SortOrder));
                }
            }
                

            return element;

        }

        /// <summary>
        ///  use the packager for the basic import 
        ///  (we do more in fix and fix)
        /// </summary>
        /// <param name="node"></param>
        public static void SyncImport(this XElement node)
        {
           LogHelper.Debug<SyncDocType>("Starting DocType Import [{0}]", ()=> node.Element("Info").Element("Alias").Value);             
           foreach (IContentType item in _packService.ImportContentTypes(node, false))
           {
               LogHelper.Debug<SyncDocType>("Imported [{0}] with {1} properties", () => item.Alias, () => item.PropertyTypes.Count());
           }
        }

        /// <summary>
        ///  import the structure of the document type.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="node"></param>
        public static void SyncImportStructure(this IContentType item, XElement node)
        {
            LogHelper.Debug<SyncDocType>("Importing Structure for {0}", () => item.Alias);

            XElement structure = node.Element("Structure");

            List<ContentTypeSort> allowed = new List<ContentTypeSort>();
            int sortOrder = 0;

            foreach (var doc in structure.Elements("DocumentType"))
            {
                string alias = doc.Value;

                if (!string.IsNullOrEmpty(alias))
                {
                    IContentType aliasDoc = ApplicationContext.Current.Services.ContentTypeService.GetContentType(alias);

                    if (aliasDoc != null)
                    {
                        allowed.Add(new ContentTypeSort(new Lazy<int>(() => aliasDoc.Id), sortOrder, aliasDoc.Name));
                        sortOrder++;
                    }
                }
            }

            item.AllowedContentTypes = allowed;
        }

        /// <summary>
        ///  puts the sortorder of the tabs in - something the default import doesn't do.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="node"></param>
        public static void SyncTabSortOrder(this IContentType item, XElement node)
        {
            XElement tabs = node.Element("Tabs");

            foreach (var tab in tabs.Elements("Tab"))
            {
                var tabId = int.Parse(tab.Element("Id").Value);
                var sortOrder = tab.Element("SortOrder");

                if (sortOrder != null)
                {
                    var itemTab = item.PropertyGroups.First(x => x.Id == tabId);
                    if (itemTab != null)
                    {
                        itemTab.SortOrder = int.Parse(sortOrder.Value);
                    }
                }

            }

        }

        public static void SyncRemoveMissingProperties(this IContentType item, XElement node)
        {
            LogHelper.Debug<SyncDocType>("Property Resolver for {0}", () => item.Alias); 

            if (!uSyncSettings.docTypeSettings.DeletePropertyValues)
            {
                LogHelper.Debug<SyncDocType>("DeletePropertyValue = false - exiting");
                return;
            }

            List<string> propertiesToRemove = new List<string>();
            Dictionary<string, string> propertiesToMove = new Dictionary<string, string>();

            foreach (var property in item.PropertyTypes)
            {
                // is this property in our xml ?
                XElement propertyNode = node.Element("GenericProperties")
                                            .Elements("GenericProperty")
                                            .Where(x => x.Element("Alias").Value == property.Alias)
                                            .SingleOrDefault();

                if (propertyNode == null)
                {
                    // delete it from the doctype ? 
                    propertiesToRemove.Add(property.Alias);
                    LogHelper.Debug<SyncDocType>("Removing property {0} from {1}", ()=> property.Alias, ()=> item.Name);

                }
                else
                {
                    // if it is we should re-write the properties - because i default import doesn't do that

                    /* 
                     *  how we work out what datatype we are...
                     */
                    var dataTypeId = new Guid(propertyNode.Element("Type").Value);
                    var dataTypeDefinitionId = new Guid(propertyNode.Element("Definition").Value);

                    IDataTypeService _dataTypeService = ApplicationContext.Current.Services.DataTypeService;

                    var dataTypeDefintion = _dataTypeService.GetDataTypeDefinitionById(dataTypeDefinitionId);

                    if (dataTypeDefintion == null || dataTypeDefintion.ControlId != dataTypeId)
                    {
                        var dataTypeDefintions = _dataTypeService.GetDataTypeDefinitionByControlId(dataTypeId);
                        if (dataTypeDefintions != null && dataTypeDefintions.Any())
                        {
                            dataTypeDefintion = dataTypeDefintions.First();
                        }
                    }

                    if (dataTypeDefintion != null)
                    {
                        property.DataTypeDefinitionId = dataTypeDefintion.Id;
                        // as you can't set property.DataTypeId or the internal DB type i'm a bit
                        // worried this might break if the type changes inside                   
                    }

                    // all the other properties.
                    property.Name = propertyNode.Element("Name").Value;
                    property.Description = propertyNode.Element("Description").Value;
                    property.Mandatory = propertyNode.Element("Mandatory").Value.ToLowerInvariant().Equals("true");
                    property.ValidationRegExp = propertyNode.Element("Validation").Value;

                    /*
                    var helpText = propertyNode.Element("HelpText");
                    if (helpText != null)
                    {
                        property.HelpText = helpText.Value;
                    }
                    */

                    var tab = propertyNode.Element("Tab").Value;
                    if (!string.IsNullOrEmpty(tab))
                    {
                        // node moving ? - that will be fun ?

                        var pg = item.PropertyGroups.First(x => x.Name == tab);

                        if (!pg.PropertyTypes.Any(x => x.Alias == property.Alias))
                        {
                            // if it's not in the group - we can move it into it*/
                            propertiesToMove.Add(property.Alias, tab);
                        }
                    }

                }
            }

            foreach (string alias in propertiesToRemove)
            {
                item.RemovePropertyType(alias);
            }

            foreach (KeyValuePair<string, string> movePair in propertiesToMove)
            {
                item.MovePropertyType(movePair.Key, movePair.Value);
            }
        }

        /// <summary>
        /// works out what the folder stucture for a doctype should be.
        /// 
        /// recurses up the parent path of the doctype, adding a folder
        /// for each one, this then gives us a structure we can create
        /// on the disk, that mimics that of the umbraco internal one
        /// </summary>
        /// <param name="item">ContentType path to find</param>
        /// <returns>folderstucture (relative to uSync folder)</returns>
        public static string GetSyncPath(this IContentType item)
        {
            string path = "";

            if (item != null)
            {
                if (item.ParentId != 0)
                {
                    path = _contentTypeService.GetContentType(item.ParentId).GetSyncPath();
                }

                path = string.Format("{0}\\{1}", path, helpers.XmlDoc.ScrubFile(item.Alias));
            }
            return path;
        }
    }
}
