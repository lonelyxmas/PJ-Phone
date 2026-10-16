namespace PJ.SoftPhoneSdk.Sip;

class PhoneLoggerWriter(IPhoneLogger logger) : LogWriter
{

    public IPhoneLogger Logger => logger;

    public override void write(LogEntry entry)
    {
        /*
         *   --* Level 0 Display fatal error only.
             * Level 1 Display error messages and more severe verbosity level only.
             * Level 2 Display Warning messages and more severe verbosity level only.
             * Level 3 Info verbosity (normally used by applications).
             * Level 4 Important PJSIP events.
             * Level 5 Detailed PJSIP events.
             --* Level 6 Very detailed PJLIB events.
         */
        switch (entry.level)
        {
            case 0:
                logger.Fatal(entry.threadId, entry.threadName, entry.msg);
                break;
            case 1:
                logger.Error(entry.threadId, entry.threadName, entry.msg);
                break;
            case 2:
                logger.Warn(entry.threadId, entry.threadName, entry.msg);
                break;
            case 3:
                logger.Info(entry.threadId, entry.threadName, entry.msg);
                break;
            case 4:
            case 5:
            case 6:
                logger.Debug(entry.threadId, entry.threadName, entry.msg);
                break;
        }
    }
}