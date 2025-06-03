#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion

//using Opc.Ua;
//using Opc.Ua.PubSub;
//using Opc.Ua.PubSub.Configuration;
//using Opc.Ua.PubSub.Transport;

namespace SmartReaderStandalone.Utils;

public class OpcUaHelper
{
    //private bool useMqttJson = true;
    //private bool useMqttUadp = false;
    //private bool useUdpUadp = false;
    //private string publisherUrl = null;
    //private static UaPubSubApplication? uaPubSubApplication;


    //public static void Init(StandaloneConfigDTO standaloneConfigDTO)
    //{
    //    string publisherUrl = null;
    //    try
    //    {
    //        //InitializeLog();

    //        PubSubConfigurationDataType pubSubConfiguration = null;
    //        // set default UDP Publisher Url to local multi-cast if not sent in args.
    //        if (string.IsNullOrEmpty(publisherUrl))
    //        {
    //            publisherUrl = standaloneConfigDTO.opcUaConnectionUrl;
    //        }

    //        // Create configuration using UDP protocol and UADP Encoding
    //        pubSubConfiguration = CreatePublisherConfiguration_UdpUadp(publisherUrl, standaloneConfigDTO);
    //        Console.WriteLine("The PubSub Connection was initialized using UDP & UADP Profile.");


    //        // Create the UA Publisher application using configuration file
    //        uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration);

    //        // Start values simulator
    //        //PublishedValuesWrites valuesSimulator = new PublishedValuesWrites(uaPubSubApplication);
    //        //valuesSimulator.Start();

    //        // Start the publisher
    //        uaPubSubApplication.Start();

    //        //Console.WriteLine("Publisher Started. Press Ctrl-C to exit...");

    //        ManualResetEvent quitEvent = new ManualResetEvent(false);

    //        //Console.CancelKeyPress += (sender, eArgs) => {
    //        //    quitEvent.Set();
    //        //    eArgs.Cancel = true;
    //        //};

    //        // wait for timeout or Ctrl-C
    //        quitEvent.WaitOne();


    //        //Console.WriteLine("Program ended.");
    //        //Console.WriteLine("Press any key to finish...");
    //        //Console.ReadKey();
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine(ex.Message);
    //    }
    //}

    ///// <summary>
    ///// Write (update) field data
    ///// </summary>
    ///// <param name="metaDatafieldName"></param>
    ///// <param name="dataValue"></param>
    //public static void WriteFieldData(string metaDatafieldName, ushort namespaceIndex, string data)
    //{
    //    Opc.Ua.DataValue dataValue = new Opc.Ua.DataValue(new Opc.Ua.Variant(data), Opc.Ua.StatusCodes.Good, DateTime.UtcNow);
    //    if (uaPubSubApplication != null)
    //    {
    //        try
    //        {
    //            //WriteFieldData("String", NamespaceIndexAllTypes, new DataValue(new Variant(m_aviationAlphabet[0]), StatusCodes.Good, DateTime.UtcNow));
    //            uaPubSubApplication.DataStore.WritePublishedDataItem(new NodeId(metaDatafieldName, namespaceIndex), Attributes.Value, dataValue);
    //        }
    //        catch (Exception)
    //        {

    //        }

    //    }

    //}

    //#region Private Methods
    ///// <summary>
    ///// Creates a PubSubConfiguration object for UDP & UADP programmatically.
    ///// </summary>
    ///// <returns></returns>
    //private static PubSubConfigurationDataType CreatePublisherConfiguration_UdpUadp(string urlAddress, StandaloneConfigDTO standaloneConfigDTO)
    //{
    //    // Define a PubSub connection with PublisherId 1
    //    PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
    //    pubSubConnection1.Name = standaloneConfigDTO.opcUaConnectionName;
    //    pubSubConnection1.Enabled = true;
    //    pubSubConnection1.PublisherId = UInt16.Parse(standaloneConfigDTO.opcUaConnectionPublisherId);
    //    pubSubConnection1.TransportProfileUri = Profiles.UadpTransport;
    //    NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
    //    // Specify the local Network interface name to be used
    //    // e.g. address.NetworkInterface = "Ethernet";
    //    // Leave empty to publish on all available local interfaces.
    //    address.NetworkInterface = String.Empty;
    //    address.Url = urlAddress;
    //    pubSubConnection1.Address = new ExtensionObject(address);

    //    // configure custom DiscoveryAddress for Discovery messages
    //    pubSubConnection1.TransportSettings = new ExtensionObject()
    //    {
    //        Body = new DatagramConnectionTransportDataType()
    //        {
    //            DiscoveryAddress = new ExtensionObject()
    //            {
    //                Body = new NetworkAddressUrlDataType()
    //                {
    //                    Url = standaloneConfigDTO.opcUaConnectionDiscoveryAddress
    //                }
    //            }
    //        }
    //    };

    //    #region Define WriterGroup1
    //    WriterGroupDataType writerGroup1 = new WriterGroupDataType();
    //    writerGroup1.Name = standaloneConfigDTO.opcUaWriterGroupName;
    //    writerGroup1.Enabled = true;
    //    writerGroup1.WriterGroupId = ushort.Parse(standaloneConfigDTO.opcUaWriterGroupId);
    //    writerGroup1.PublishingInterval = double.Parse(standaloneConfigDTO.opcUaWriterPublishingInterval);
    //    writerGroup1.KeepAliveTime = double.Parse(standaloneConfigDTO.opcUaWriterKeepAliveTime);
    //    writerGroup1.MaxNetworkMessageSize = uint.Parse(standaloneConfigDTO.opcUaWriterMaxNetworkMessageSize);
    //    writerGroup1.HeaderLayoutUri = standaloneConfigDTO.opcUaWriterHeaderLayoutUri;
    //    UadpWriterGroupMessageDataType uadpMessageSettings = new UadpWriterGroupMessageDataType()
    //    {
    //        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
    //        GroupVersion = 0,
    //        NetworkMessageContentMask = (uint)(UadpNetworkMessageContentMask.PublisherId
    //                | UadpNetworkMessageContentMask.GroupHeader
    //                | UadpNetworkMessageContentMask.PayloadHeader // needed to be able to decode the DataSetWriterId
    //                | UadpNetworkMessageContentMask.WriterGroupId
    //                | UadpNetworkMessageContentMask.GroupVersion
    //                | UadpNetworkMessageContentMask.NetworkMessageNumber
    //                | UadpNetworkMessageContentMask.SequenceNumber)
    //    };

    //    writerGroup1.MessageSettings = new ExtensionObject(uadpMessageSettings);
    //    // initialize Datagram (UDP) Transport Settings
    //    writerGroup1.TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType());

    //    // Define DataSetWriter 'Simple'
    //    DataSetWriterDataType dataSetWriter1 = new DataSetWriterDataType();
    //    dataSetWriter1.Name = standaloneConfigDTO.opcUaDataSetWriterName;
    //    dataSetWriter1.DataSetWriterId = ushort.Parse(standaloneConfigDTO.opcUaDataSetWriterId);
    //    dataSetWriter1.Enabled = true;
    //    dataSetWriter1.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
    //    dataSetWriter1.DataSetName = standaloneConfigDTO.opcUaDataSetName;
    //    dataSetWriter1.KeyFrameCount = uint.Parse(standaloneConfigDTO.opcUaDataSetKeyFrameCount);
    //    UadpDataSetWriterMessageDataType uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType()
    //    {
    //        NetworkMessageNumber = 1,
    //        DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
    //    };

    //    dataSetWriter1.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
    //    writerGroup1.DataSetWriters.Add(dataSetWriter1);

    //    // Define DataSetWriter 'AllTypes'
    //    DataSetWriterDataType dataSetWriter2 = new DataSetWriterDataType();
    //    dataSetWriter2.Name = "Writer 2";
    //    dataSetWriter2.DataSetWriterId = 2;
    //    dataSetWriter2.Enabled = true;
    //    dataSetWriter2.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
    //    dataSetWriter2.DataSetName = "AllTypes";
    //    dataSetWriter2.KeyFrameCount = 1;
    //    uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType()
    //    {
    //        NetworkMessageNumber = 1,
    //        DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
    //    };

    //    dataSetWriter2.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
    //    writerGroup1.DataSetWriters.Add(dataSetWriter2);

    //    pubSubConnection1.WriterGroups.Add(writerGroup1);
    //    #endregion

    //    //  Define PublishedDataSet Simple
    //    PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

    //    // Define PublishedDataSet AllTypes
    //    PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

    //    //create  the PubSub configuration root object
    //    PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
    //    pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
    //        {
    //            pubSubConnection1
    //        };
    //    pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection()
    //        {
    //            publishedDataSetSimple, publishedDataSetAllTypes
    //        };

    //    return pubSubConfiguration;
    //}


    ///// <summary>
    ///// Creates the "Simple" DataSet
    ///// </summary>
    ///// <returns></returns>
    //private static PublishedDataSetDataType CreatePublishedDataSetSimple()
    //{
    //    PublishedDataSetDataType publishedDataSetSimple = new PublishedDataSetDataType();
    //    publishedDataSetSimple.Name = "Simple"; //name shall be unique in a configuration
    //    // Define  publishedDataSetSimple.DataSetMetaData
    //    publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType();
    //    publishedDataSetSimple.DataSetMetaData.DataSetClassId = Uuid.Empty;
    //    publishedDataSetSimple.DataSetMetaData.Name = publishedDataSetSimple.Name;
    //    publishedDataSetSimple.DataSetMetaData.Fields = new FieldMetaDataCollection()
    //        {
    //            new FieldMetaData()
    //            {
    //                Name = "String",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.String,
    //                DataType = DataTypeIds.String,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "BoolToggle",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Boolean,
    //                DataType = DataTypeIds.Boolean,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Int32",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Int32,
    //                DataType = DataTypeIds.Int32,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Int32Fast",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Int32,
    //                DataType = DataTypeIds.Int32,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "DateTime",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.DateTime,
    //                DataType = DataTypeIds.DateTime,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //        };
    //    DateTimeOffset dt = new DateTimeOffset();
    //    // set the ConfigurationVersion relative to kTimeOfConfiguration constant
    //    publishedDataSetSimple.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
    //    {
    //        MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(dt.Date),
    //        MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(dt.Date)
    //    };

    //    PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
    //    publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
    //    //create PublishedData based on metadata names
    //    foreach (var field in publishedDataSetSimple.DataSetMetaData.Fields)
    //    {
    //        publishedDataSetSimpleSource.PublishedData.Add(
    //            new PublishedVariableDataType()
    //            {
    //                PublishedVariable = new NodeId(field.Name, 2),
    //                AttributeId = Attributes.Value,
    //            });
    //    }

    //    publishedDataSetSimple.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

    //    return publishedDataSetSimple;
    //}

    ///// <summary>
    ///// Creates the "AllTypes" DataSet
    ///// </summary>
    ///// <returns></returns>
    //private static PublishedDataSetDataType CreatePublishedDataSetAllTypes()
    //{
    //    PublishedDataSetDataType publishedDataSetAllTypes = new PublishedDataSetDataType();
    //    publishedDataSetAllTypes.Name = "AllTypes"; //name shall be unique in a configuration
    //    // Define  publishedDataSetAllTypes.DataSetMetaData
    //    publishedDataSetAllTypes.DataSetMetaData = new DataSetMetaDataType();
    //    publishedDataSetAllTypes.DataSetMetaData.DataSetClassId = Uuid.Empty;
    //    publishedDataSetAllTypes.DataSetMetaData.Name = publishedDataSetAllTypes.Name;
    //    publishedDataSetAllTypes.DataSetMetaData.Fields = new FieldMetaDataCollection()
    //        {
    //            new FieldMetaData()
    //            {
    //                Name = "BoolToggle",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Boolean,
    //                DataType = DataTypeIds.Boolean,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Byte",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Byte,
    //                DataType = DataTypeIds.Byte,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Int16",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Int16,
    //                DataType = DataTypeIds.Int16,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Int32",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Int32,
    //                DataType = DataTypeIds.Int32,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "SByte",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.SByte,
    //                DataType = DataTypeIds.SByte,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "UInt16",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.UInt16,
    //                DataType = DataTypeIds.UInt16,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "UInt32",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                 BuiltInType = (byte)DataTypes.UInt32,
    //                DataType = DataTypeIds.UInt32,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "UInt64",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                 BuiltInType = (byte)DataTypes.UInt64,
    //                DataType = DataTypeIds.UInt64,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Float",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Float,
    //                DataType = DataTypeIds.Float,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Double",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Double,
    //                DataType = DataTypeIds.Double,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "String",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.String,
    //                DataType = DataTypeIds.String,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "ByteString",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.ByteString,
    //                DataType = DataTypeIds.ByteString,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "Guid",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.Guid,
    //                DataType = DataTypeIds.Guid,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "DateTime",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.DateTime,
    //                DataType = DataTypeIds.DateTime,
    //                ValueRank = ValueRanks.Scalar
    //            },
    //            new FieldMetaData()
    //            {
    //                Name = "UInt32Array",
    //                DataSetFieldId = new Uuid(Guid.NewGuid()),
    //                BuiltInType = (byte)DataTypes.UInt32,
    //                DataType = DataTypeIds.UInt32,
    //                ValueRank = ValueRanks.OneDimension
    //            },
    //        };

    //    DateTimeOffset dt = new DateTimeOffset();
    //    // set the ConfigurationVersion relative to kTimeOfConfiguration constant
    //    publishedDataSetAllTypes.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
    //    {
    //        MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(dt.Date),
    //        MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(dt.Date)
    //    };
    //    PublishedDataItemsDataType publishedDataSetAllTypesSource = new PublishedDataItemsDataType();

    //    //create PublishedData based on metadata names
    //    foreach (var field in publishedDataSetAllTypes.DataSetMetaData.Fields)
    //    {
    //        publishedDataSetAllTypesSource.PublishedData.Add(
    //            new PublishedVariableDataType() 
    //            {
    //                PublishedVariable = new NodeId(field.Name, 3),
    //                AttributeId = Attributes.Value,
    //            });
    //    }
    //    publishedDataSetAllTypes.DataSetSource = new ExtensionObject(publishedDataSetAllTypesSource);

    //    return publishedDataSetAllTypes;
    //}

    //#endregion
}