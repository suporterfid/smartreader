namespace SmartReaderStandalone.Services
{
    public interface ISummaryQueueBackgroundService
    {
        bool HasDataAvailable();
        void AddData(string jsonData);
        string GetData();
        void StartQueue();
        void StopQueue();
        //void DecrementConnections();
        //void IncrementConnections();
        bool IsService(Type serviceType);
    }
}