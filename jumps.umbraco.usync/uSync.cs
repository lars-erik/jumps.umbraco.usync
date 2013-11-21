﻿//
// uSync 1.7.0beta

// For Umbraco 6.1.x
//
// uses precompile conditions to build a v4 and v6 version
// of usync. 
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO; // so we can write to disk..
using System.Xml; // so we can serialize stuff

using Umbraco.Core;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;

using System.Diagnostics; 

namespace jumps.umbraco.usync
{
    public delegate void uSyncBulkEventHander(uSyncEventArgs e);
    /// <summary>
    /// usync. the umbraco database to disk and back again helper.
    /// 
    /// first thing, lets register ourselfs with the umbraco install
    /// </summary>
    public class uSync : IApplicationEventHandler 
    {
        // mutex stuff, so we only do this once.
        private static object _syncObj = new object(); 
        private static bool _synced = false ; 
        
        // TODO: Config this
        private bool _read;
        private bool _write;
        private bool _attach;

        // our own events - fired when we start and stop
        public static event uSyncBulkEventHander Starting ; 
        public static event uSyncBulkEventHander Initialized ; 
        
        /// <summary>
        /// do the stuff we do when we start, using locks, and flags so
        /// we only do the stuff once..
        /// </summary>
        private void DoOnStart()
        {

            // lock
            if (!_synced)
            {
                lock (_syncObj)
                {
                    if (!_synced)
                    {
                        // everything we do here is blocking
                        // on application start, so we should be 
                        // quick. 
                        GetSettings(); 

                        RunSync();
                        
                        _synced = true;
                    }
                }
            }
        }

        private void GetSettings() 
        {
            LogHelper.Debug<uSync>("Getting Settings"); 
                       
            _read = uSyncSettings.Read;
            LogHelper.Debug<uSync>("Settings : Read = {0}", ()=> _read); 

            _write = uSyncSettings.Write;
            LogHelper.Debug<uSync>("Settings : Write = {0}", ()=> _write); 

            _attach = uSyncSettings.Attach;
            LogHelper.Debug<uSync>("Settings : Attach = {0}", ()=> _attach); 

            // version 6+ here
        }

        /// <summary>
        /// save everything in the DB to disk. 
        /// </summary>
        public void SaveAllToDisk()
        {
            LogHelper.Debug<uSync>("Saving to disk - start");
            Stopwatch _swSave = Stopwatch.StartNew(); 

            if ( uSyncSettings.Elements.DocumentTypes ) 
                SyncDocType.SaveAllToDisk();

            if ( uSyncSettings.Elements.Macros ) 
                SyncMacro.SaveAllToDisk();

            if ( uSyncSettings.Elements.MediaTypes ) 
                SyncMediaTypes.SaveAllToDisk();

            if ( uSyncSettings.Elements.Templates ) 
                SyncTemplate.SaveAllToDisk();

            if ( uSyncSettings.Elements.Stylesheets ) 
                SyncStylesheet.SaveAllToDisk();

            if (uSyncSettings.Elements.DataTypes)
            {
#if UMBRACO7
                SyncDataTypesV7.SaveAllToDisk(); 
#else 
                SyncDataType.SaveAllToDisk();
#endif
            }

            if (uSyncSettings.Elements.Dictionary)
            {
                SyncLanguage.SaveAllToDisk();
                SyncDictionary.SaveAllToDisk();
            }
            _swSave.Stop();
            LogHelper.Info<uSync>("All Elements Saved [{0} miliseconds]", () => _swSave.Elapsed.TotalMilliseconds);
            LogHelper.Debug<uSync>("Saving to Disk - End"); 
        }

        /// <summary>
        /// read all settings from disk and sync to the database
        /// </summary>
        public void ReadAllFromDisk()
        {
            LogHelper.Debug<uSync>("Reading from Disk - starting");
            Stopwatch _swRead = Stopwatch.StartNew(); 

            if ( uSyncSettings.Elements.Templates ) 
                SyncTemplate.ReadAllFromDisk();

            LogHelper.Info<uSync>("Templates [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            if ( uSyncSettings.Elements.Stylesheets ) 
                SyncStylesheet.ReadAllFromDisk();

            LogHelper.Info<uSync>("Stylesheets [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            if (uSyncSettings.Elements.DataTypes)
            {
#if UMBRACO7
                SyncDataTypesV7.ReadAllFromDisk(); 
#else
                SyncDataType.ReadAllFromDisk();
#endif
            }

            LogHelper.Info<uSync>("DataTypes [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            if ( uSyncSettings.Elements.DocumentTypes ) 
                SyncDocType.ReadAllFromDisk();

            LogHelper.Info<uSync>("DocumentTypes [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            if ( uSyncSettings.Elements.Macros ) 
                SyncMacro.ReadAllFromDisk();

            LogHelper.Info<uSync>("Macros [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            if ( uSyncSettings.Elements.MediaTypes ) 
                SyncMediaTypes.ReadAllFromDisk();

            LogHelper.Info<uSync>("MeidaTypes [{0}]", () => _swRead.Elapsed.TotalMilliseconds);


            if (uSyncSettings.Elements.Dictionary)
            {
                SyncLanguage.ReadAllFromDisk(); 
                SyncDictionary.ReadAllFromDisk();
            }

            LogHelper.Info<uSync>("Dictionary [{0}]", () => _swRead.Elapsed.TotalMilliseconds);

            _swRead.Stop();
            LogHelper.Info<uSync>("All read from Disk [{0} miliseconds]", ()=> _swRead.Elapsed.TotalMilliseconds);

            LogHelper.Debug<uSync>("Reading from Disk - End"); 
        }

        /// <summary>
        /// attach to the onSave and onDelete event for all types
        /// </summary>
        public void AttachToAll()
        {
            LogHelper.Debug<uSync>("Attaching to Events - Start");

            if (uSyncSettings.Elements.DataTypes)
            {
#if UMBRACO7
                SyncDataTypesV7.AttachEvents();
#else 
                SyncDataType.AttachEvents();
#endif
            }

            if ( uSyncSettings.Elements.DocumentTypes )
                SyncDocType.AttachEvents();

            if ( uSyncSettings.Elements.MediaTypes ) 
                SyncMediaTypes.AttachEvents();

            if ( uSyncSettings.Elements.Macros ) 
                SyncMacro.AttachEvents();

            if ( uSyncSettings.Elements.Templates ) 
                SyncTemplate.AttachEvents();

            if ( uSyncSettings.Elements.Stylesheets ) 
                SyncStylesheet.AttachEvents();

            if (uSyncSettings.Elements.Dictionary)
            {
                SyncLanguage.AttachEvents(); 
                SyncDictionary.AttachEvents();
            }

            LogHelper.Debug<uSync>("Attaching to Events - End");
        }

        /// <summary>
        ///  run through the first sync (called at startup)
        /// </summary>
        private void RunSync()
        {
            Stopwatch sw = Stopwatch.StartNew(); 

            LogHelper.Info<uSync>("uSync Starting - for detailed debug info. set priority to 'Debug' in log4net.config file");

            OnStarting(new uSyncEventArgs(_read, _write, _attach)); 

            if (!ApplicationContext.Current.IsConfigured)
            {
                LogHelper.Info<uSync>("umbraco not configured, usync aborting");
                return;
            }

            // Save Everything to disk.
            // only done first time or when write = true           
            if (!Directory.Exists(IOHelper.MapPath(helpers.uSyncIO.RootFolder)) || _write )
            {
                SaveAllToDisk();
            }
                        
            //
            // we take the disk and sync it to the DB, this is how 
            // you can then distribute using uSync.
            //

            if (_read)
            {
                if (!File.Exists(Path.Combine(IOHelper.MapPath(helpers.uSyncIO.RootFolder), "usync.stop")))
                {

                    ReadAllFromDisk(); 

                    if (File.Exists(Path.Combine(IOHelper.MapPath(helpers.uSyncIO.RootFolder), "usync.once")))
                    {
                        LogHelper.Debug<uSync>("Renaming once file"); 
                        File.Move(Path.Combine(IOHelper.MapPath(helpers.uSyncIO.RootFolder), "usync.once"),
                            Path.Combine(IOHelper.MapPath(helpers.uSyncIO.RootFolder), "usync.stop"));
                        LogHelper.Debug<uSync>("Once renamed to stop"); 
                    }
                }
                else
                {
                    LogHelper.Info<uSync>("Read stopped by usync.stop"); 
                }

            }

            if (_attach)
            {
                // everytime. register our events to all the saves..
                // that way we capture things as they are done.
                AttachToAll(); 
            }

            sw.Stop(); 
            LogHelper.Info<uSync>("uSync Initilized [{0} milliseconds]", ()=> sw.Elapsed.TotalMilliseconds);
            OnComplete(new uSyncEventArgs(_read, _write, _attach));
        }

        public void OnApplicationStarted(UmbracoApplicationBase httpApplication, Umbraco.Core.ApplicationContext applicationContext)
        {
            DoOnStart();
        }

        public void OnApplicationStarting(UmbracoApplicationBase httpApplication, Umbraco.Core.ApplicationContext applicationContext)
        {
            // don't think i do it here.
        }

        public void OnApplicationInitialized(UmbracoApplicationBase httpApplication, Umbraco.Core.ApplicationContext applicationContext)
        {
            // don't think i do it here.
        }

        // our events
        public static void OnStarting(uSyncEventArgs e)
        {
            if (Starting != null)
            {
                Starting(e);
            }
        }

        public static void OnComplete(uSyncEventArgs e)
        {
            if (Initialized != null)
            {
                Initialized(e);
            }
        }


        // some API Points (for other apps to fire)
    }

    /// <summary>
    /// event firing - arguments when we fire. 
    /// </summary>
    public class uSyncEventArgs : EventArgs
    {
        private bool _import;
        private bool _export;
        private bool _attach;

        public uSyncEventArgs(bool import, bool export, bool attach)
        {
            _import = import;
            _export = export;
            _attach = attach;
        }

        public bool Import
        {
            get { return _import; }
        }

        public bool Export
        {
            get { return _export; }
        }

        public bool Attach
        {
            get { return _attach; }
        }
    }
}
