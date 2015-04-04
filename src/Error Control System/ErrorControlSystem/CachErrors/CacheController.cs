using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ErrorControlSystem.DbConnectionManager;
using ErrorControlSystem.Properties;
using ErrorControlSystem.ServerController;
using ErrorControlSystem.Shared;

namespace ErrorControlSystem.CachErrors
{
    internal static class CacheController
    {
        public static int ExpireHours = 100; // after 100H of first logged error time, all errors sent from cache to server

        public static volatile bool AreErrorsInSendState = false;

        public static ActionBlock<Tuple<ProxyError, bool>> AcknowledgeActionBlock;

        private static ActionBlock<Error> _errorSaverActionBlock;

        #region Methods


        static CacheController()
        {
            #region Acknowledge Action Block

            AcknowledgeActionBlock = new ActionBlock<Tuple<ProxyError, bool>>(
                async ack =>
                {
                    if (ack.Item2) // Error Successful sent to server database
                    {
                        //
                        // Remove Error from Log file:
                        await SdfFileManager.DeleteAsync(ack.Item1.Id);
                        //
                        // De-story error from Memory (RAM):
                        if (ack.Item1 != null) ack.Item1.Dispose();
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxMessagesPerTask = 1
                });

            #endregion
        }


        /// <summary>
        /// Check Cache State to Send Data to Server or Not ?
        /// </summary>
        public static async Task CheckStateAsync()
        {
            if (AreErrorsInSendState) return;

            try
            {
                AreErrorsInSendState = true;

                // C:\Users\[User Name.Domain]\AppData\Local\MyApp\
                // Example ==> C:\Users\khosravifar.b.DBI\AppData\Local\AppName vAppVersion
                var rootDir = StorageRouter.ErrorLogFilePath.Substring(0, StorageRouter.ErrorLogFilePath.LastIndexOf('\\'));

                var maxSize = Settings.Default.CacheLimitSize;


                // if errors caching data was larger than limited size then send it to server 
                // and if successful sent then clear them...
                if (ConnectionManager.GetDefaultConnection().IsReady && ServerTransmitter.CanToSent)
                    if (new DirectoryInfo(rootDir).GetDirectorySize() >= maxSize
                        || await SdfFileManager.GetTheFirstErrorHoursAsync() >= ExpireHours)
                    {
                        await UploadCacheAsync();
                    }
            }
            finally
            {
                AreErrorsInSendState = false;
            }
        }

        public static async Task UploadCacheAsync()
        {
            foreach (var error in SdfFileManager.GetErrors())
            {
                await ServerTransmitter.ErrorListenerTransformBlock.SendAsync(new ProxyError(error));

                if (!ServerTransmitter.CanToSent) break;
            }
        }

        public static async void CacheTheError(Error error, ErrorHandlingOptions option)
        {
            if (_errorSaverActionBlock == null ||
                _errorSaverActionBlock.Completion.IsFaulted)
            {
                #region Initile Action Block Again

                _errorSaverActionBlock = new ActionBlock<Error>(async e =>
                {
                    if (await SdfFileManager.InsertOrUpdateAsync(e)) // insert or update database and return cache check state
                        if (_errorSaverActionBlock.InputCount == 0 && option.HasFlag(ErrorHandlingOptions.SendCacheToServer))
                            await CheckStateAsync();
                },
                    new ExecutionDataflowBlockOptions
                    {
                        MaxMessagesPerTask = 1
                    });

                #endregion
            }

            await _errorSaverActionBlock.SendAsync(error);
        }

        #endregion
    }
}