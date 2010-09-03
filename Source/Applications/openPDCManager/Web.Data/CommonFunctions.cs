﻿//******************************************************************************************************
//  CommonFunctions.cs - Gbtc
//
//  Copyright © 2010, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  07/05/2009 - Mehulbhai Thakkar
//       Generated original version of source code.
//  09/15/2009 - Stephen C. Wills
//       Added new header and license agreement.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Soap;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using openPDCManager.Data.BusinessObjects;
using openPDCManager.Data.Entities;
using openPDCManager.Data.ServiceCommunication;
using TVA;
using TVA.PhasorProtocols;

namespace openPDCManager.Data
{
    /// <summary>
    /// Class that defines common operations on data (retrieval and update)
    /// </summary>
    public static class CommonFunctions
    {
        static DataSet GetResultSet(IDbCommand command)		//This function was added because at few places mySQL complained about foreign key constraints which I was not able to figure out.
        {
            //TODO: Find a way to get rid of this function for mySQL.
            DataSet dataSet = new DataSet();
            DataTable dataTable = new DataTable();
            dataSet.EnforceConstraints = false;
            dataSet.Tables.Add(dataTable);
            dataTable.Load(command.ExecuteReader());
            return dataSet;
        }

        public static string GetReturnMessage(string source, string userMessage, string systemMessage, string detail, MessageType userMessageType)
        {
            Message message = new Message();
            message.Source = source;
            message.UserMessage = userMessage;
            message.SystemMessage = systemMessage;
            message.Detail = detail;
            message.UserMessageType = userMessageType;
            
            MemoryStream memoryStream = new MemoryStream();
            XmlSerializer xs = new XmlSerializer(typeof(Message));
            XmlTextWriter xmlTextWriter = new XmlTextWriter(memoryStream, Encoding.UTF8);
            xs.Serialize(xmlTextWriter, message);
            memoryStream = (MemoryStream)xmlTextWriter.BaseStream;
            return (new UTF8Encoding()).GetString(memoryStream.ToArray());
        }

        public static List<ErrorLog> ReadExceptionLog(int numberOfLogs)
        {
            DataConnection connection = new DataConnection();            
            try
            {
                List<ErrorLog> errorLogList = new List<ErrorLog>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select Top " + numberOfLogs.ToString() + " * From ErrorLog Order By CreatedOn DESC";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                errorLogList = (from item in resultTable.AsEnumerable()
                                select new ErrorLog()
                                {
                                    ID = item.Field<int>("ID"),
                                    Source = item.Field<string>("Source"),
                                    Message = item.Field<string>("Message"),
                                    CreatedOn = Convert.ToDateTime(item.Field<object>("CreatedOn")),
                                    Detail = item.Field<string>("Detail")
                                }).ToList();
                return errorLogList;
            }
            finally
            {
                connection.Dispose();
            }
        }

        public static void LogException(string source, Exception ex)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Insert Into ErrorLog (Source, Message, Detail) Values (@source, @message, @detail)";
                command.Parameters.Add(AddWithValue(command, "@source", source));
                command.Parameters.Add(AddWithValue(command, "@message", ex.Message));
                command.Parameters.Add(AddWithValue(command, "@detail", ex.ToString()));
                command.ExecuteNonQuery();
            }
            catch
            {
                //Do nothing. Dont worry about it.
            }
            finally
            {
                connection.Dispose();
            }
        }

        public static string GetExecutingAssemblyPath()
        {
            return TVA.IO.FilePath.GetAbsolutePath("Temp");
        }

        public static string SaveIniFile(Stream input)
        {			
            string fileName = Guid.NewGuid().ToString() + ".ini";
            using (FileStream fs = File.Create(GetExecutingAssemblyPath() + "\\" + fileName))
            {
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    fs.Write(buffer, 0, bytesRead);
                }
            }
            return fileName;		
        }

        public static ConnectionSettings GetConnectionSettings(Stream inputStream)
        {
            ConnectionSettings connectionSettings = new ConnectionSettings();
        
            SoapFormatter sf = new SoapFormatter();
            sf.AssemblyFormat = FormatterAssemblyStyle.Simple;
            sf.TypeFormat = FormatterTypeStyle.TypesWhenNeeded;
            sf.Binder = new VersionConfigToNamespaceAssemblyObjectBinder();
            connectionSettings = sf.Deserialize(inputStream) as ConnectionSettings;

            if (connectionSettings.ConnectionParameters != null)
            {
                ConnectionSettings cs = new ConnectionSettings();
                cs = (ConnectionSettings)connectionSettings.ConnectionParameters;
                connectionSettings.configurationFileName = cs.configurationFileName;
                connectionSettings.refreshConfigurationFileOnChange = cs.refreshConfigurationFileOnChange;
                connectionSettings.parseWordCountFromByte = cs.parseWordCountFromByte;
            }

            return connectionSettings;			
        }

        public static List<WizardDeviceInfo> GetWizardConfigurationInfo(Stream inputStream)
        {				
            SoapFormatter sf = new SoapFormatter();
            sf.AssemblyFormat = FormatterAssemblyStyle.Simple;
            sf.TypeFormat = FormatterTypeStyle.TypesWhenNeeded;
            IConfigurationFrame configurationFrame = sf.Deserialize(inputStream) as IConfigurationFrame;								
            return ParseConfigurationFrame(configurationFrame);
        }

        public static List<WizardDeviceInfo> ParseConfigurationFrame(IConfigurationFrame configurationFrame)
        {
            //try
            //{
                List<WizardDeviceInfo> wizardDeviceInfoList = new List<WizardDeviceInfo>();
                if (configurationFrame != null)
                {
                    int parentAccessID = configurationFrame.IDCode;
                    wizardDeviceInfoList = (from cell in configurationFrame.Cells
                                            select new WizardDeviceInfo()
                                            {
                                                Acronym = cell.StationName.Replace(" ", "").ToUpper(),
                                                Name = CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(cell.StationName.ToLower()),
                                                Longitude = -98.6m,
                                                Latitude = 37.5m,
                                                VendorDeviceID = (int?)null,
                                                AccessID = cell.IDCode,
                                                ParentAccessID = parentAccessID,
                                                Include = true,
                                                DigitalCount = cell.DigitalDefinitions.Count(),
                                                AnalogCount = cell.AnalogDefinitions.Count(),
                                                AddDigitals = false,
                                                AddAnalogs = false,
                                                IsNew = GetDeviceByAcronym(cell.StationName.Replace(" ", "").ToUpper()) == null ? true : false,
                                                PhasorList = new ObservableCollection<PhasorInfo>( (from phasor in cell.PhasorDefinitions
                                                              select new PhasorInfo()
                                                              {
                                                                  Label = CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(phasor.Label.Replace("?", " ").Trim().ToLower()),
                                                                  Type = phasor.PhasorType == PhasorType.Current ? "I" : "V",
                                                                  Phase = "+",
                                                                  DestinationLabel = "",
                                                                  Include = true
                                                              }).ToList())
                                            }).ToList();

                }

                List<string> nondistinctAcronymList = new List<string>();
                nondistinctAcronymList = (from item in wizardDeviceInfoList                                          
                                          group item by item.Acronym into grouped
                                          where grouped.Count() > 1
                                          select grouped.Key).ToList();

                if (nondistinctAcronymList.Count > 0)
                {
                    int i = 0;
                    foreach (WizardDeviceInfo deviceInfo in wizardDeviceInfoList)
                    {
                        if (deviceInfo.IsNew && nondistinctAcronymList.Contains(deviceInfo.Acronym))
                        {
                            deviceInfo.Acronym = deviceInfo.Acronym.Substring(0, deviceInfo.Acronym.Length - i.ToString().Length) + i.ToString();
                            i++;
                        }
                    }                    
                }

                return wizardDeviceInfoList;
            //}
            //catch (Exception ex)
            //{
            //    LogException("ParseConfigurationFrame", ex);
            //    CustomServiceFault fault = new CustomServiceFault() { UserMessage = "Failed to Parse Configuration Frame", SystemMessage = ex.Message };
            //    throw new FaultException<CustomServiceFault>(fault);
            //}
        }

        private static IConfigurationFrame s_responseAttachment;
        public static string s_responseMessage;
        private static ManualResetEvent s_responseWaitHandle;
        public static List<WizardDeviceInfo> RetrieveConfigurationFrame(string nodeConnectionString, string deviceConnectionString, int protocolID)
        {
            s_responseMessage = string.Empty;
            s_responseAttachment = null;
            WindowsServiceClient windowsServiceClient = new WindowsServiceClient(nodeConnectionString);
            try
            {
                s_responseWaitHandle = new ManualResetEvent(false);
                windowsServiceClient.Helper.ReceivedServiceResponse += new EventHandler<TVA.EventArgs<TVA.Services.ServiceResponse>>(Helper_ReceivedServiceResponse);
                windowsServiceClient.Helper.ReceivedServiceUpdate += new EventHandler<TVA.EventArgs<TVA.Services.UpdateType, string>>(Helper_ReceivedServiceUpdate);
                windowsServiceClient.Helper.RemotingClient.MaxConnectionAttempts = 10;
                windowsServiceClient.Helper.Connect();
                if (windowsServiceClient.Helper.RemotingClient.Enabled)
                {
                    if (deviceConnectionString.ToLower().Contains("phasorprotocol"))
                        windowsServiceClient.Helper.SendRequest(string.Format("invoke 0 requestdeviceconfiguration \"{0}\"", deviceConnectionString));
                    else
                        windowsServiceClient.Helper.SendRequest(string.Format("invoke 0 requestdeviceconfiguration \"{0}\"", deviceConnectionString + "; phasorProtocol=" + GetProtocolAcronymByID(protocolID)));

                    if (s_responseWaitHandle.WaitOne(65000))
                    {
                        if (s_responseAttachment is ConfigurationErrorFrame)
                            throw new ApplicationException("Received configuration error frame, invocation request for device configuration has failed.\r\n");
                        else if (s_responseAttachment is IConfigurationFrame)
                            return ParseConfigurationFrame(s_responseAttachment as IConfigurationFrame);						
                        else
                            throw new ApplicationException("Invalid frame received, invocation for device configuration has failed.");
                    }
                    else
                        throw new ApplicationException("Response timeout occured. Waited 60 seconds for Configuration Frame to arrive.");
                }
                else
                {
                    throw new ApplicationException("Connection timeout occured. Tried 10 times to connect to openPDC windows service.");
                }
            }			
            finally
            {
                windowsServiceClient.Helper.Disconnect();
                //windowsServiceClient.Dispose();
            }
        }

        static void Helper_ReceivedServiceUpdate(object sender, TVA.EventArgs<TVA.Services.UpdateType, string> e)
        {
            if (e.Argument2.StartsWith("[PHASOR!SERVICES]") && !e.Argument2.Contains("*"))
                s_responseMessage += e.Argument2.Replace("[PHASOR!SERVICES]", "").Replace("\r\n\r\n", "\r\n");			
        }

        private static void Helper_ReceivedServiceResponse(object sender, TVA.EventArgs<TVA.Services.ServiceResponse> e)
        {			           
            List<object> attachments = e.Argument.Attachments;

            // Handle any special attachments coming in from service
            if (attachments != null)
            {
                foreach (object attachment in attachments)
                {
                    if (attachment is ConfigurationErrorFrame)
                    {
                        Thread.Sleep(1000);
                        s_responseAttachment = attachment as ConfigurationErrorFrame;
                        s_responseWaitHandle.Set();
                        //Console.WriteLine("Received configuration error frame, invocation request for device configuration has failed. See common phasor services response for reason.\r\n");
                    }
                    else if (attachment is IConfigurationFrame)	// If user requested a configuration frame, send it to parsing function to retrieve devices list.
                    {
                        s_responseAttachment = attachment as IConfigurationFrame;
                        s_responseWaitHandle.Set();
                    }
                }
            }
        }

        public static string SaveWizardConfigurationInfo(string nodeID, List<WizardDeviceInfo> wizardDeviceInfoList, string connectionString, 
                int? protocolID, int? companyID, int? historianID, int? interconnectionID, int? parentID, bool skipDisableRealTimeData)
        {
            try
            {
                List<string> nondistinctAcronymList = new List<string>();
                nondistinctAcronymList = (from item in wizardDeviceInfoList
                                          where item.Include == true
                                          group item by item.Acronym into grouped
                                          where grouped.Count() > 1
                                          select grouped.Key).ToList();

                if (nondistinctAcronymList.Count > 0)
                {
                    StringBuilder sb = new StringBuilder("Duplicate Acronyms Exist");
                    foreach (string item in nondistinctAcronymList)
                    {
                        sb.AppendLine();
                        sb.Append(item);
                    }
                    throw new ArgumentException(sb.ToString());
                }

                int loadOrder = 1;
                foreach (WizardDeviceInfo info in wizardDeviceInfoList)
                {
                    if (info.Include)
                    {
                        Device device = new Device();
                        device.NodeID = nodeID;
                        device.Acronym = info.Acronym;
                        device.Name = info.Name;
                        device.IsConcentrator = false;
                        device.Longitude = info.Longitude;
                        device.Latitude = info.Latitude;
                        device.ConnectionString = parentID == null ? connectionString : string.Empty;
                        device.ProtocolID = protocolID;
                        device.CompanyID = companyID;
                        device.HistorianID = historianID;
                        device.InterconnectionID = interconnectionID;
                        device.Enabled = true;
                        device.VendorDeviceID = info.VendorDeviceID == null ? (int?)null : info.VendorDeviceID == 0 ? (int?)null : info.VendorDeviceID;
                        device.ParentID = parentID;
                        device.TimeZone = string.Empty;
                        device.AccessID = info.AccessID;
                        //Please review from here.										
                        device.TimeAdjustmentTicks = 0;
                        device.DataLossInterval = 5;
                        device.MeasuredLines = 1;
                        device.LoadOrder = loadOrder;
                        device.ContactList = string.Empty;
                        device.AllowedParsingExceptions = 10;
                        device.ParsingExceptionWindow = 5;
                        device.DelayedConnectionInterval = 5;
                        device.AllowUseOfCachedConfiguration = true;
                        device.AutoStartDataParsingSequence = true;
                        device.SkipDisableRealTimeData = skipDisableRealTimeData;
                        device.MeasurementReportingInterval = 100000;

                        //If Add Digitals and Add Analogs is checked for the device then, if digitals and analogs are available i.e. count>0 then add them as measurements.		
                        int digitalCount = 0;
                        if (info.AddDigitals && info.DigitalCount > 0)
                        {
                            digitalCount = info.DigitalCount;
                        }
                        int analogCount = 0;
                        if (info.AddAnalogs && info.AnalogCount > 0)
                        {
                            analogCount = info.AnalogCount;
                        }

                        Device existingDevice = GetDeviceByAcronym(info.Acronym);
                        if (existingDevice != null)
                        {
                            device.ID = existingDevice.ID;
                            device.TimeZone = existingDevice.TimeZone;
                            device.TimeAdjustmentTicks = existingDevice.TimeAdjustmentTicks;
                            device.DataLossInterval = existingDevice.DataLossInterval;
                            device.MeasuredLines = existingDevice.MeasuredLines;
                            device.ContactList = existingDevice.ContactList;
                            SaveDevice(device, false, digitalCount, analogCount);
                        }
                        else
                            SaveDevice(device, true, digitalCount, analogCount);

                        Device addedDevice = GetDeviceByAcronym(info.Acronym);
                        int count = 1;
                        foreach (PhasorInfo phasorInfo in info.PhasorList)
                        {
                            if (phasorInfo.Label.ToLower() != "unused")
                            {
                                Phasor phasor = new Phasor();
                                phasor.DeviceID = addedDevice.ID;
                                phasor.Label = phasorInfo.Label;
                                phasor.Type = phasorInfo.Type;
                                phasor.Phase = phasorInfo.Phase;
                                phasor.DestinationPhasorID = null;
                                phasor.SourceIndex = count;

                                Phasor existingPhasor = GetPhasorBySourceIndex(addedDevice.ID, phasor.SourceIndex);
                                if (existingPhasor != null)
                                {
                                    phasor.ID = existingPhasor.ID;
                                    SavePhasor(phasor, false);
                                }
                                else
                                    SavePhasor(phasor, true);
                            }
                            count++;
                        }
                    }
                    loadOrder++;
                }
                return "Configuration Information Saved Successfully";
            }
            finally { }
        }

        public static IDbDataParameter AddWithValue(IDbCommand command, string name, object value)
        {
            return AddWithValue(command, name, value, ParameterDirection.Input);
        }
        
        public static IDbDataParameter AddWithValue(IDbCommand command, string name, object value, ParameterDirection direction)
        {
            IDbDataParameter param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            param.Direction = direction;			
            return param;
        }

        public static bool MasterNode(string nodeID)
        {	
            bool isMaster = false;
            DataConnection connection = new DataConnection();
            try
            {	
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select Master From Node Where ID = @id";
                command.Parameters.Add(AddWithValue(command, "@id", nodeID));
                isMaster = Convert.ToBoolean(command.ExecuteScalar());				
            }
            catch (Exception ex)
            {
                isMaster = false;
                LogException("MasterNode", ex);
            }
            finally
            {
                connection.Dispose();
            }
            return isMaster;
        }

        public static Dictionary<string, string> GetTimeZones(bool isOptional)
        {
            Dictionary<string, string> timeZonesList = new Dictionary<string, string>();
                if (isOptional)
                    timeZonesList.Add("", "Select Timezone");

                foreach (TimeZoneInfo tzi in TimeZoneInfo.GetSystemTimeZones())
                {					
                    if (!timeZonesList.ContainsKey(tzi.StandardName))
                        timeZonesList.Add(tzi.StandardName, tzi.DisplayName);

                    System.Diagnostics.Debug.WriteLine(tzi.DisplayName);
                }
                return timeZonesList;
        }

        public static Dictionary<string, int> GetVendorDeviceDistribution(string nodeID)
        {
            DataConnection connection = new DataConnection();
            Dictionary<string, int> deviceDistribution = new Dictionary<string, int>();
            try
            {
                if (!string.IsNullOrEmpty(nodeID))
                {
                    IDbCommand command = connection.Connection.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = "Select * From VendorDeviceDistribution WHERE NodeID = @nodeID Order By VendorName";
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));

                    DataTable resultTable = new DataTable();
                    resultTable.Load(command.ExecuteReader());

                    foreach (DataRow row in resultTable.Rows)
                    {
                        deviceDistribution.Add(row["VendorName"].ToString(), Convert.ToInt32(row["DeviceCount"]));
                    }
                }			
            }
            catch (Exception ex)
            {
                LogException("GetVendorDeviceDistribution", ex);
                //CustomServiceFault fault = new CustomServiceFault() { UserMessage = "Failed to Get Vendor Device Distribution", SystemMessage = ex.Message };
                //throw new FaultException<CustomServiceFault>(fault);
            }
            finally
            {
                connection.Dispose();
            }
            return deviceDistribution;
        }

        public static List<InterconnectionStatus> GetInterconnectionStatus(string nodeID)
        {
            DataConnection connection = new DataConnection();
            List<InterconnectionStatus> interConnectionStatusList = new List<InterconnectionStatus>();
            try
            {
                if (!string.IsNullOrEmpty(nodeID))
                {
                    //throw new ArgumentException("Invalid value of NodeID.");
                    DataSet resultSet = new DataSet();
                    resultSet.Tables.Add(new DataTable("InterconnectionSummary"));
                    resultSet.Tables.Add(new DataTable("MemberSummary"));

                    IDbCommand command1 = connection.Connection.CreateCommand();
                    command1.CommandType = CommandType.Text;
                    command1.CommandText = "Select InterconnectionName, Count(*) AS DeviceCount From DeviceDetail WHERE NodeID = @nodeID Group By InterconnectionName";
                    if (command1.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command1.Parameters.Add(AddWithValue(command1, "@nodeID", "{" + nodeID + "}"));
                    else
                        command1.Parameters.Add(AddWithValue(command1, "@nodeID", nodeID));
                    resultSet.Tables["InterconnectionSummary"].Load(command1.ExecuteReader());

                    IDbCommand command2 = connection.Connection.CreateCommand();
                    command2.CommandType = CommandType.Text;
                    command2.CommandText = "Select CompanyAcronym, CompanyName, InterconnectionName, Count(*) AS DeviceCount, Sum(MeasuredLines) AS MeasuredLines " +
                                            "From DeviceDetail WHERE NodeID = @nodeID Group By CompanyAcronym, CompanyName, InterconnectionName";
                    if (command2.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command2.Parameters.Add(AddWithValue(command2, "@nodeID", "{" + nodeID + "}"));
                    else
                        command2.Parameters.Add(AddWithValue(command2, "@nodeID", nodeID));
                    resultSet.Tables["MemberSummary"].Load(command2.ExecuteReader());

                    interConnectionStatusList = (from item in resultSet.Tables["InterconnectionSummary"].AsEnumerable()
                                                 select new InterconnectionStatus()
                                                 {
                                                     InterConnection = item.Field<string>("InterconnectionName"),
                                                     TotalDevices = "Total " + item.Field<object>("DeviceCount").ToString() + " Devices",
                                                     MemberStatusList = (from cs in resultSet.Tables["MemberSummary"].AsEnumerable()
                                                                         where cs.Field<string>("InterconnectionName") == item.Field<string>("InterconnectionName")
                                                                         select new MemberStatus()
                                                                         {
                                                                             CompanyAcronym = cs.Field<string>("CompanyAcronym"),
                                                                             CompanyName = cs.Field<string>("CompanyName"),
                                                                             MeasuredLines = Convert.ToInt32(cs.Field<object>("MeasuredLines")),
                                                                             TotalDevices = Convert.ToInt32(cs.Field<object>("DeviceCount"))
                                                                         }).ToList()
                                                 }).ToList();
                }
            }
            catch (Exception ex)
            {
                LogException("GetInterconnectionStatus", ex);
                //CustomServiceFault fault = new CustomServiceFault() { UserMessage = "Failed to Get Interconnection Status", SystemMessage = ex.Message };
                //throw new FaultException<CustomServiceFault>(fault);
            }
            finally
            {
                connection.Dispose();
            }
            return interConnectionStatusList;
        }

        public static List<TimeSeriesDataPoint> GetTimeSeriesData(string timeSeriesDataUrl)
        {				
            List<TimeSeriesDataPoint> timeSeriesData = new List<TimeSeriesDataPoint>();
            try
            {				
                HttpWebRequest request = WebRequest.Create(timeSeriesDataUrl) as HttpWebRequest;				
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        StreamReader reader = new StreamReader(response.GetResponseStream());						
                        XElement timeSeriesDataPoints = XElement.Parse(reader.ReadToEnd());
                        long index = 0;

                        foreach (XElement element in timeSeriesDataPoints.Element("TimeSeriesDataPoints").Elements("TimeSeriesDataPoint"))
                        {
                            timeSeriesData.Add(new TimeSeriesDataPoint()
                                {
                                    Index = index,
                                    Value = Convert.ToDouble(element.Element("Value").Value)
                                });
                            index = index + 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException("GetRealTimeData", ex);
                //throw ex;
            }

            return timeSeriesData;
        }

        public static List<TimeSeriesDataPointDetail> GetTimeSeriesDataDetail(string timeSeriesDataUrl)
        {
            List<TimeSeriesDataPointDetail> timeSeriesData = new List<TimeSeriesDataPointDetail>();
            try
            {
                HttpWebRequest request = WebRequest.Create(timeSeriesDataUrl) as HttpWebRequest;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        StreamReader reader = new StreamReader(response.GetResponseStream());
                        XElement timeSeriesDataPoints = XElement.Parse(reader.ReadToEnd());
                        
                        foreach (XElement element in timeSeriesDataPoints.Element("TimeSeriesDataPoints").Elements("TimeSeriesDataPoint"))
                        {
                            timeSeriesData.Add(new TimeSeriesDataPointDetail()
                            {                        
                                TimeStamp =  element.Element("Time").Value,
                                Value = Convert.ToDouble(element.Element("Value").Value),
                                Quality = element.Element("Quality").Value
                            });                            
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException("GetRealTimeDataDetail", ex);
                //throw ex;
            }

            return timeSeriesData;
        }
                
        public static Dictionary<int, TimeTaggedMeasurement> GetTimeTaggedMeasurements(string timeSeriesDataUrl)
        {
            Dictionary<int, TimeTaggedMeasurement> timeTaggedMeasurementList = new Dictionary<int, TimeTaggedMeasurement>();

            try
            {
                HttpWebRequest request = WebRequest.Create(timeSeriesDataUrl) as HttpWebRequest;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        StreamReader reader = new StreamReader(response.GetResponseStream());
                        XElement timeSeriesDataPoints = XElement.Parse(reader.ReadToEnd());

                        foreach (XElement element in timeSeriesDataPoints.Element("TimeSeriesDataPoints").Elements("TimeSeriesDataPoint"))
                        {                            
                            timeTaggedMeasurementList.Add(Convert.ToInt32(element.Element("HistorianID").Value), new TimeTaggedMeasurement()
                            {
                                //PointID = Convert.ToInt32(element.Element("HistorianID").Value),
                                TimeTag = element.Element("Time").Value,
                                CurrentValue = element.Element("Value").Value,
                                Quality = element.Element("Quality").Value
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException("GetTimeTaggedMeasurements", ex);
            }

            return timeTaggedMeasurementList;
        }

        public static Dictionary<int, TimeTaggedMeasurement> GetStatisticMeasurements(string statisticDataUrl, string nodeID)
        {
            Dictionary<int, TimeTaggedMeasurement> statisticMeasurementList = new Dictionary<int, TimeTaggedMeasurement>();
            Dictionary<int, BasicStatisticInfo> basicStatisticList = new Dictionary<int, BasicStatisticInfo>(GetBasicStatisticInfoList(nodeID));

            try
            {
                HttpWebRequest request = WebRequest.Create(statisticDataUrl) as HttpWebRequest;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        StreamReader reader = new StreamReader(response.GetResponseStream());
                        XElement timeSeriesDataPoints = XElement.Parse(reader.ReadToEnd());

                        foreach (XElement element in timeSeriesDataPoints.Element("TimeSeriesDataPoints").Elements("TimeSeriesDataPoint"))
                        {
                            BasicStatisticInfo basicStatisticInfo;
                            if (basicStatisticList.TryGetValue(Convert.ToInt32(element.Element("HistorianID").Value), out basicStatisticInfo))
                            {
                                statisticMeasurementList.Add(Convert.ToInt32(element.Element("HistorianID").Value), new TimeTaggedMeasurement()
                                {
                                    //PointID = Convert.ToInt32(element.Element("HistorianID").Value),
                                    TimeTag = element.Element("Time").Value,
                                    CurrentValue = string.Format(basicStatisticInfo.DisplayFormat, ConvertValueToType(element.Element("Value").Value, basicStatisticInfo.DataType)),
                                    Quality = element.Element("Quality").Value
                                });
                            }                           
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException("GetTimeTaggedMeasurements", ex);
            }

            return statisticMeasurementList;
        }

        static object ConvertValueToType(string xmlValue, string xmlDataType)
        {
            Type dataType = Type.GetType(xmlDataType);
            float value;

            if (float.TryParse(xmlValue, out value))
            {
                switch (xmlDataType)
                {
                    case "System.DateTime":
                        return new DateTime((long)value);
                    default:
                        return Convert.ChangeType(value, dataType);
                }
            }
            
            return "".ConvertToType(dataType);
        }

        public static KeyValuePair<int, int> GetMinMaxPointIDs(string nodeID)
        {
            KeyValuePair<int, int> minMaxPointIDs = new KeyValuePair<int, int>(1, 5000);
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select MIN(PointID) AS MinPointID, MAX(PointID) AS MaxPointID From MeasurementDetail Where NodeID = @nodeID";
                if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                else
                    command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {

                    minMaxPointIDs = new KeyValuePair<int, int>(reader["MinPointID"] == System.DBNull.Value ? 0 : reader.GetInt32(0), reader["MaxPointID"] == System.DBNull.Value ? 0 : reader.GetInt32(1));
                }
            }
            catch (Exception ex)
            {
                LogException("GetMinMaxPointIDs", ex);
            }
            finally
            {
                connection.Dispose();
            }

            return minMaxPointIDs;
        }
                
        //public static List<string> GetPorts()
        //{
        //    List<string> portsList = new List<string>();

        //    foreach (string port in System.IO.Ports.SerialPort.GetPortNames())
        //        portsList.Add(port);

        //    return portsList;
        //}

        public static List<string> GetStopBits()
        {
            List<string> stopBitsList = new List<string>();

            foreach (string stopBit in Enum.GetNames(typeof(System.IO.Ports.StopBits)))
                stopBitsList.Add(stopBit);

            return stopBitsList;
        }

        public static List<string> GetParities()
        {
            List<string> parityList = new List<string>();

            foreach (string parity in Enum.GetNames(typeof(System.IO.Ports.Parity)))
                parityList.Add(parity);

            return parityList;
        }

        #region " Manage Companies Code"

        public static List<Company> GetCompanyList()
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Company> companyList = new List<Company>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "SELECT ID, Acronym, MapAcronym, Name, URL, LoadOrder FROM Company ORDER BY LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                companyList = (from item in resultTable.AsEnumerable()
                               select new Company()
                               {
                                   ID = item.Field<int>("ID"),
                                   Acronym = item.Field<string>("Acronym"),
                                   MapAcronym = item.Field<string>("MapAcronym"),
                                   Name = item.Field<string>("Name"),
                                   URL = item.Field<string>("URL"),
                                   LoadOrder = item.Field<int>("LoadOrder")
                               }).ToList();
                return companyList;
            }
            finally
            {
                connection.Dispose();
            }			
        }

        public static Dictionary<int, string> GetCompanies(bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> companyList = new Dictionary<int, string>();
                if (isOptional)
                    companyList.Add(0, "Select Company");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "SELECT ID, Name FROM Company ORDER BY LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                int id;
                foreach (DataRow row in resultTable.Rows)
                {
                    id = int.Parse(row["ID"].ToString());

                    if (!companyList.ContainsKey(id))
                        companyList.Add(id, row["Name"].ToString());
                }
                return companyList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveCompany(Company company, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "INSERT INTO Company (Acronym, MapAcronym, Name, URL, LoadOrder) VALUES (@acronym, @mapAcronym, @name, @url, @loadOrder)";
                else
                    command.CommandText = "UPDATE Company SET Acronym = @acronym, MapAcronym = @mapAcronym, Name = @name, URL = @url, LoadOrder = @loadOrder WHERE ID = @id";

                command.Parameters.Add(AddWithValue(command, "@acronym", company.Acronym));
                command.Parameters.Add(AddWithValue(command, "@mapAcronym", company.MapAcronym));
                command.Parameters.Add(AddWithValue(command, "@name", company.Name));
                command.Parameters.Add(AddWithValue(command, "@url", company.URL));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", company.LoadOrder));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", company.ID));

                command.ExecuteNonQuery();				
                return "Company Information Saved Successfully";
            }
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Output Streams Code"

        public static List<OutputStream> GetOutputStreamList(bool enabledOnly, string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStream> outputStreamList = new List<OutputStream>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (enabledOnly)
                {
                    command.CommandText = "SELECT * FROM OutputStreamDetail Where NodeID = @nodeID AND Enabled = @enabled ORDER BY LoadOrder";
                    command.Parameters.Add(AddWithValue(command, "@enabled", true));
                }
                else
                    command.CommandText = "SELECT * FROM OutputStreamDetail Where NodeID = @nodeID ORDER BY LoadOrder";

                //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                else
                    command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamList = (from item in resultTable.AsEnumerable()
                                    select new OutputStream()
                                    {
                                        NodeID = item.Field<object>("NodeID").ToString(),
                                        ID = item.Field<int>("ID"),
                                        Acronym = item.Field<string>("Acronym"),
                                        Name = item.Field<string>("Name"),
                                        Type = item.Field<int>("Type"),
                                        ConnectionString = item.Field<string>("ConnectionString"),
                                        IDCode = item.Field<int>("IDCode"),
                                        CommandChannel = item.Field<string>("CommandChannel"),
                                        DataChannel = item.Field<string>("DataChannel"),
                                        AutoPublishConfigFrame = Convert.ToBoolean(item.Field<object>("AutoPublishConfigFrame")),
                                        AutoStartDataChannel = Convert.ToBoolean(item.Field<object>("AutoStartDataChannel")),
                                        NominalFrequency = item.Field<int>("NominalFrequency"),
                                        FramesPerSecond = Convert.ToInt32(item.Field<object>("FramesPerSecond") ?? 30),
                                        LagTime = item.Field<double>("LagTime"),
                                        LeadTime = item.Field<double>("LeadTime"),
                                        UseLocalClockAsRealTime = Convert.ToBoolean(item.Field<object>("UseLocalClockAsRealTime")),
                                        AllowSortsByArrival = Convert.ToBoolean(item.Field<object>("AllowSortsByArrival")),
                                        LoadOrder = item.Field<int>("LoadOrder"),
                                        Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                        NodeName = item.Field<string>("NodeName"),
                                        TypeName = item.Field<int>("Type") == 0 ? "IEEE C37.118" : "BPA",
                                        IgnoreBadTimeStamps = Convert.ToBoolean(item.Field<object>("IgnoreBadTimeStamps")),
                                        TimeResolution = Convert.ToInt32(item.Field<object>("TimeResolution")),
                                        AllowPreemptivePublishing = Convert.ToBoolean(item.Field<object>("AllowPreemptivePublishing")),
                                        DownsamplingMethod = item.Field<string>("DownsamplingMethod"),
                                        DataFormat = item.Field<string>("DataFormat"),
                                        CoordinateFormat = item.Field<string>("CoordinateFormat"),
                                        CurrentScalingValue = Convert.ToInt32(item.Field<object>("CurrentScalingValue")),
                                        VoltageScalingValue = Convert.ToInt32(item.Field<object>("VoltageScalingValue")),
                                        AnalogScalingValue = Convert.ToInt32(item.Field<object>("AnalogScalingValue")),
                                        DigitalMaskValue = Convert.ToInt32(item.Field<object>("DigitalMaskValue"))
                                    }).ToList();
                return outputStreamList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveOutputStream(OutputStream outputStream, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "INSERT INTO OutputStream (NodeID, Acronym, Name, Type, ConnectionString, IDCode, CommandChannel, DataChannel, AutoPublishConfigFrame, AutoStartDataChannel, NominalFrequency, FramesPerSecond, LagTime, LeadTime, " +
                                        "UseLocalClockAsRealTime, AllowSortsByArrival, LoadOrder, Enabled, IgnoreBadTimeStamps, TimeResolution, AllowPreemptivePublishing, DownsamplingMethod, DataFormat, CoordinateFormat, CurrentScalingValue, VoltageScalingValue, " +
                                        "AnalogScalingValue, DigitalMaskValue) VALUES (@nodeID, @acronym, @name, @type, @connectionString, @idCode, @commandChannel, @dataChannel, @autoPublishConfigFrame, @autoStartDataChannel, @nominalFrequency, @framesPerSecond, " +
                                        "@lagTime, @leadTime, @useLocalClockAsRealTime, @allowSortsByArrival, @loadOrder, @enabled, @ignoreBadTimeStamps, @timeResolution, @allowPreemptivePublishing, @downsamplingMethod, @dataFormat, @coordinateFormat, " +
                                        "@currentScalingValue, @voltageScalingValue, @analogScalingValue, @digitalMaskValue)";
                else
                    command.CommandText = "UPDATE OutputStream SET NodeID = @nodeID, Acronym = @acronym, Name = @name, Type = @type, ConnectionString = @connectionString, IDCode = @idCode, CommandChannel = @commandChannel, DataChannel = @dataChannel, AutoPublishConfigFrame = @autoPublishConfigFrame, " +
                                        "AutoStartDataChannel = @autoStartDataChannel, NominalFrequency = @nominalFrequency, FramesPerSecond = @framesPerSecond, LagTime = @lagTime, LeadTime = @leadTime, UseLocalClockAsRealTime = @useLocalClockAsRealTime, " +
                                        "AllowSortsByArrival = @allowSortsByArrival, LoadOrder = @loadOrder, Enabled = @enabled, IgnoreBadTimeStamps = @ignoreBadTimeStamps, TimeResolution = @timeResolution, AllowPreemptivePublishing = @allowPreemptivePublishing, " +
                                        "DownsamplingMethod = @downsamplingMethod, DataFormat = @dataFormat, CoordinateFormat = @coordinateFormat, CurrentScalingValue = @currentScalingValue, VoltageScalingValue = @voltageScalingValue, " +
                                        "AnalogScalingValue = @analogScalingValue, DigitalMaskValue = @digitalMaskValue WHERE ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStream.NodeID));
                command.Parameters.Add(AddWithValue(command, "@acronym", outputStream.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", outputStream.Name));
                command.Parameters.Add(AddWithValue(command, "@type", outputStream.Type));
                command.Parameters.Add(AddWithValue(command, "@connectionString", outputStream.ConnectionString));
                command.Parameters.Add(AddWithValue(command, "@idCode", outputStream.IDCode));
                command.Parameters.Add(AddWithValue(command, "@commandChannel", outputStream.CommandChannel));
                command.Parameters.Add(AddWithValue(command, "@dataChannel", outputStream.DataChannel));
                command.Parameters.Add(AddWithValue(command, "@autoPublishConfigFrame", outputStream.AutoPublishConfigFrame));
                command.Parameters.Add(AddWithValue(command, "@autoStartDataChannel", outputStream.AutoStartDataChannel));
                command.Parameters.Add(AddWithValue(command, "@nominalFrequency", outputStream.NominalFrequency));
                command.Parameters.Add(AddWithValue(command, "@framesPerSecond", outputStream.FramesPerSecond));
                command.Parameters.Add(AddWithValue(command, "@lagTime", outputStream.LagTime));
                command.Parameters.Add(AddWithValue(command, "@leadTime", outputStream.LeadTime));
                command.Parameters.Add(AddWithValue(command, "@useLocalClockAsRealTime", outputStream.UseLocalClockAsRealTime));
                command.Parameters.Add(AddWithValue(command, "@allowSortsByArrival", outputStream.AllowSortsByArrival));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", outputStream.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", outputStream.Enabled));
                command.Parameters.Add(AddWithValue(command, "@ignoreBadTimeStamps", outputStream.IgnoreBadTimeStamps));
                command.Parameters.Add(AddWithValue(command, "@timeResolution", outputStream.TimeResolution));
                command.Parameters.Add(AddWithValue(command, "@allowPreemptivePublishing", outputStream.AllowPreemptivePublishing));
                command.Parameters.Add(AddWithValue(command, "@downsamplingMethod", outputStream.DownsamplingMethod));
                command.Parameters.Add(AddWithValue(command, "@dataFormat", outputStream.DataFormat));
                command.Parameters.Add(AddWithValue(command, "@coordinateFormat", outputStream.CoordinateFormat));
                command.Parameters.Add(AddWithValue(command, "@currentScalingValue", outputStream.CurrentScalingValue));
                command.Parameters.Add(AddWithValue(command, "@voltageScalingValue", outputStream.VoltageScalingValue));
                command.Parameters.Add(AddWithValue(command, "@analogScalingValue", outputStream.AnalogScalingValue));
                command.Parameters.Add(AddWithValue(command, "@digitalMaskValue", outputStream.DigitalMaskValue));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", outputStream.ID));

                command.ExecuteNonQuery();

                return "Output Stream Information Saved Successfully";
            }
            
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Output Stream Measurements Code"

        public static List<OutputStreamMeasurement> GetOutputStreamMeasurementList(int outputStreamID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStreamMeasurement> outputStreamMeasurementList = new List<OutputStreamMeasurement>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From OutputStreamMeasurementDetail Where AdapterID = @adapterID";
                command.Parameters.Add(AddWithValue(command, "@adapterID", outputStreamID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamMeasurementList = (from item in resultTable.AsEnumerable()
                                               select new OutputStreamMeasurement()
                                               {
                                                   NodeID = item.Field<object>("NodeID").ToString(),
                                                   AdapterID = item.Field<int>("AdapterID"),
                                                   ID = item.Field<int>("ID"),
                                                   PointID = item.Field<int>("PointID"),
                                                   HistorianID = item.Field<int?>("HistorianID"),
                                                   SignalReference = item.Field<string>("SignalReference"),
                                                   SourcePointTag = item.Field<string>("SourcePointTag"),
                                                   HistorianAcronym = item.Field<string>("HistorianAcronym")
                                               }).ToList();
                return outputStreamMeasurementList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveOutputStreamMeasurement(OutputStreamMeasurement outputStreamMeasurement, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into OutputStreamMeasurement (NodeID, AdapterID, HistorianID, PointID, SignalReference) " +
                        "Values (@nodeID, @adapterID, @historianID, @pointID, @signalReference)";
                else
                    command.CommandText = "Update OutputStreamMeasurement Set NodeID = @nodeID, AdapterID = @adapterID, HistorianID = @historianID, " +
                        "PointID = @pointID, SignalReference = @signalReference WHERE ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStreamMeasurement.NodeID));
                command.Parameters.Add(AddWithValue(command, "@adapterID", outputStreamMeasurement.AdapterID));
                command.Parameters.Add(AddWithValue(command, "@historianID", outputStreamMeasurement.HistorianID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@pointID", outputStreamMeasurement.PointID));
                command.Parameters.Add(AddWithValue(command, "@signalReference", outputStreamMeasurement.SignalReference));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", outputStreamMeasurement.ID));

                command.ExecuteNonQuery();
                return "Output Stream Measurement Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string DeleteOutputStreamMeasurement(int outputStreamMeasurementID)	// we can just use ID column in the database for delete as it is auto increament.
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Delete From OutputStreamMeasurement Where ID = @id";
                command.Parameters.Add(AddWithValue(command, "@id", outputStreamMeasurementID));				
                command.ExecuteNonQuery();

                return "Output Stream Measurement Deleted Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Output Stream Devices Code"

        public static List<OutputStreamDevice> GetOutputStreamDeviceList(int outputStreamID, bool enabledOnly)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStreamDevice> outputStreamDeviceList = new List<OutputStreamDevice>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (!enabledOnly)
                    command.CommandText = "Select * From OutputStreamDeviceDetail Where AdapterID = @adapterID";
                else
                    command.CommandText = "Select * From OutputStreamDeviceDetail Where AdapterID = @adapterID AND Enabled = @enabled";

                command.Parameters.Add(AddWithValue(command, "@adapterID", outputStreamID));
                if (enabledOnly)
                    command.Parameters.Add(AddWithValue(command, "@enabled", true));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamDeviceList = (from item in resultTable.AsEnumerable()
                                          select new OutputStreamDevice()
                                          {
                                              NodeID = item.Field<object>("NodeID").ToString(),
                                              AdapterID = item.Field<int>("AdapterID"),
                                              ID = item.Field<int>("ID"),
                                              Acronym = item.Field<string>("Acronym"),
                                              Name = item.Field<string>("Name"),
                                              BpaAcronym = item.Field<string>("BpaAcronym"),
                                              LoadOrder = item.Field<int>("LoadOrder"),
                                              Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                              PhasorDataFormat = item.Field<string>("PhasorDataFormat"),
                                              FrequencyDataFormat = item.Field<string>("FrequencyDataFormat"),
                                              AnalogDataFormat = item.Field<string>("AnalogDataFormat"),
                                              CoordinateFormat = item.Field<string>("CoordinateFormat"),
                                              Virtual = Convert.ToBoolean(item.Field<object>("Virtual"))
                                          }).ToList();
                return outputStreamDeviceList;
            }		
            finally
            {
                connection.Dispose();
            }			
        }

        public static OutputStreamDevice GetOutputStreamDevice(int outputStreamID, string acronym)
        {
            try
            {
                List<OutputStreamDevice> ouputStreamDeviceList = new List<OutputStreamDevice>();
                ouputStreamDeviceList = (from item in GetOutputStreamDeviceList(outputStreamID, false)
                                         where item.Acronym == acronym
                                         select item).ToList();
                if (ouputStreamDeviceList.Count > 0)
                    return ouputStreamDeviceList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetOutputStreamDevice", ex);
                return null;
            }
        }

        public static string SaveOutputStreamDevice(OutputStreamDevice outputStreamDevice, bool isNew, string originalAcronym)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into OutputStreamDevice (NodeID, AdapterID, Acronym, BpaAcronym, Name, LoadOrder, Enabled, PhasorDataFormat, FrequencyDataFormat, AnalogDataFormat, CoordinateFormat) " +
                        "Values (@nodeID, @adapterID, @acronym, @bpaAcronym, @name, @loadOrder, @enabled, @phasorDataFormat, @frequencyDataFormat, @analogDataFormat, @coordinateFormat)";
                else
                    command.CommandText = "Update OutputStreamDevice Set NodeID = @nodeID, AdapterID = @adapterID, Acronym = @acronym, " +
                        "BpaAcronym = @bpaAcronym, Name = @name, LoadOrder = @loadOrder, Enabled = @enabled, PhasorDataFormat = @phasorDataFormat, " +
                        "FrequencyDataFormat = @frequencyDataFormat, AnalogDataFormat = @analogDataFormat, CoordinateFormat = @coordinateFormat Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStreamDevice.NodeID));
                command.Parameters.Add(AddWithValue(command, "@adapterID", outputStreamDevice.AdapterID));
                command.Parameters.Add(AddWithValue(command, "@acronym", outputStreamDevice.Acronym));
                command.Parameters.Add(AddWithValue(command, "@bpaAcronym", outputStreamDevice.BpaAcronym));
                command.Parameters.Add(AddWithValue(command, "@name", outputStreamDevice.Name));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", outputStreamDevice.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", outputStreamDevice.Enabled));
                command.Parameters.Add(AddWithValue(command, "@phasorDataFormat", outputStreamDevice.PhasorDataFormat));
                command.Parameters.Add(AddWithValue(command, "@frequencyDataFormat", outputStreamDevice.FrequencyDataFormat));
                command.Parameters.Add(AddWithValue(command, "@analogDataFormat", outputStreamDevice.AnalogDataFormat));
                command.Parameters.Add(AddWithValue(command, "@coordinateFormat", outputStreamDevice.CoordinateFormat));

                if (!isNew)
                {
                    command.Parameters.Add(AddWithValue(command, "@id", outputStreamDevice.ID));

                    //if output stream device is updated then modify signal references in the measurement table
                    //to reflect changes in the acronym of the device.
                    if (!string.IsNullOrEmpty(originalAcronym))
                    {
                        IDbCommand command1 = connection.Connection.CreateCommand();
                        command1.CommandType = CommandType.Text;
                        command1.CommandText = "Update OutputStreamMeasurement Set SignalReference = Replace(SignalReference, @originalAcronym, @newAcronym) Where AdapterID = @adapterID";	// and SignalReference LIKE @signalReference";
                        command1.Parameters.Add(AddWithValue(command1, "@originalAcronym", originalAcronym));
                        command1.Parameters.Add(AddWithValue(command1, "@newAcronym", outputStreamDevice.Acronym));
                        command1.Parameters.Add(AddWithValue(command1, "@adapterID", outputStreamDevice.AdapterID));
                        //command.Parameters.Add(AddWithValue(command1, "@signalReference", "%" + originalAcronym + "-%"));
                        command1.ExecuteNonQuery();
                    }
                }

                command.ExecuteNonQuery();
                return "Output Stream Device Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string DeleteOutputStreamDevice(int outputStreamID, List<string> devicesToBeDeleted)
        {
            DataConnection connection = new DataConnection();
            try
            {
                foreach (string acronym in devicesToBeDeleted)
                {
                    IDbCommand command = connection.Connection.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = "Delete From OutputStreamMeasurement Where AdapterID = @outputStreamID And SignalReference LIKE @signalReference";
                    command.Parameters.Add(AddWithValue(command, "@outputStreamID", outputStreamID));
                    command.Parameters.Add(AddWithValue(command, "@signalReference", "%" + acronym + "%"));
                    command.ExecuteNonQuery();

                    IDbCommand command1 = connection.Connection.CreateCommand();
                    command1.CommandType = CommandType.Text;
                    command1.CommandText = "Delete From OutputStreamDevice Where Acronym = @acronym And AdapterID = @adapterID";
                    command1.Parameters.Add(AddWithValue(command1, "@acronym", acronym));
                    command1.Parameters.Add(AddWithValue(command1, "@adapterID", outputStreamID));
                    command1.ExecuteNonQuery();
                }

                return "Output Stream Device Deleted Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string digitalLabel = "DIGITAL0        DIGITAL1        DIGITAL2        DIGITAL3        DIGITAL4        DIGITAL5        DIGITAL6        DIGITAL7        DIGITAL8        DIGITAL9        DIGITAL10       DIGITAL11       DIGITAL12       DIGITAL13       DIGITAL14       DIGITAL15       ";
        public static string AddDevices(int outputStreamID, Dictionary<int, string> devicesToBeAdded, bool addDigitals, bool addAnalogs)
        {
            try
            {
                foreach (KeyValuePair<int, string> deviceInfo in devicesToBeAdded)	//loop through all the devices that needs to be added.
                {
                    Device device = new Device();
                    device = GetDeviceByDeviceID(deviceInfo.Key);	//Get alll the information about the device to be added.
                    OutputStreamDevice outputStreamDevice = new OutputStreamDevice();
                    outputStreamDevice.NodeID = device.NodeID;
                    outputStreamDevice.AdapterID = outputStreamID;
                    outputStreamDevice.Acronym = device.Acronym;
                    outputStreamDevice.BpaAcronym = string.Empty;
                    outputStreamDevice.Name = device.Name;
                    outputStreamDevice.LoadOrder = device.LoadOrder;
                    outputStreamDevice.Enabled = true;
                    outputStreamDevice.PhasorDataFormat = string.Empty;
                    outputStreamDevice.FrequencyDataFormat = string.Empty;
                    outputStreamDevice.AnalogDataFormat = string.Empty;
                    outputStreamDevice.CoordinateFormat = string.Empty;
                    SaveOutputStreamDevice(outputStreamDevice, true, string.Empty);	//save in to OutputStreamDevice Table.

                    int savedOutputStreamDeviceID = GetOutputStreamDevice(outputStreamID, device.Acronym).ID;


                    //********************************************
                    List<Phasor> phasorList = new List<Phasor>();
                    phasorList = GetPhasorList(deviceInfo.Key);			//Get all the phasor information for the device to be added.

                    foreach (Phasor phasor in phasorList)
                    {
                        OutputStreamDevicePhasor outputStreamDevicePhasor = new OutputStreamDevicePhasor(); //Add all phasors one by one into OutputStreamDevicePhasor table.
                        outputStreamDevicePhasor.NodeID = device.NodeID;
                        outputStreamDevicePhasor.OutputStreamDeviceID = savedOutputStreamDeviceID;
                        outputStreamDevicePhasor.Label = phasor.Label;
                        outputStreamDevicePhasor.Type = phasor.Type;
                        outputStreamDevicePhasor.Phase = phasor.Phase;
                        outputStreamDevicePhasor.LoadOrder = phasor.SourceIndex;
                        outputStreamDevicePhasor.ScalingValue = 0;
                        SaveOutputStreamDevicePhasor(outputStreamDevicePhasor, true);
                    }
                    //********************************************

                    //********************************************
                    List<Measurement> measurementList = new List<Measurement>();
                    measurementList = GetMeasurementsByDevice(deviceInfo.Key);

                    int analogIndex = 0;
                    foreach (Measurement measurement in measurementList)
                    {
                        OutputStreamMeasurement outputStreamMeasurement = new OutputStreamMeasurement();
                        outputStreamMeasurement.NodeID = device.NodeID;
                        outputStreamMeasurement.AdapterID = outputStreamID;
                        outputStreamMeasurement.HistorianID = measurement.HistorianID;
                        outputStreamMeasurement.PointID = measurement.PointID;
                        outputStreamMeasurement.SignalReference = measurement.SignalReference;

                        if (measurement.SignalSuffix == "AV")
                        {
                            if (addAnalogs)
                            {
                                SaveOutputStreamMeasurement(outputStreamMeasurement, true);

                                OutputStreamDeviceAnalog outputStreamDeviceAnalog = new OutputStreamDeviceAnalog();
                                outputStreamDeviceAnalog.NodeID = device.NodeID;
                                outputStreamDeviceAnalog.OutputStreamDeviceID = savedOutputStreamDeviceID;
                                outputStreamDeviceAnalog.Label = device.Acronym.Length > 12 ? device.Acronym.Substring(0, 12) + ":A" + analogIndex.ToString() : device.Acronym + ":A" + analogIndex.ToString(); // measurement.PointTag;
                                outputStreamDeviceAnalog.Type = 0;	//default
                                outputStreamDeviceAnalog.LoadOrder = Convert.ToInt32(measurement.SignalReference.Substring((measurement.SignalReference.LastIndexOf("-") + 3)));
                                outputStreamDeviceAnalog.ScalingValue = 0;
                                SaveOutputStreamDeviceAnalog(outputStreamDeviceAnalog, true);
                                analogIndex += 1;
                            }
                        }
                        else if (measurement.SignalSuffix == "DV")
                        {
                            if (addDigitals)
                            {
                                SaveOutputStreamMeasurement(outputStreamMeasurement, true);

                                OutputStreamDeviceDigital outputStreamDeviceDigital = new OutputStreamDeviceDigital();
                                outputStreamDeviceDigital.NodeID = device.NodeID;
                                outputStreamDeviceDigital.OutputStreamDeviceID = savedOutputStreamDeviceID;
                                outputStreamDeviceDigital.Label = digitalLabel;     // measurement.PointTag;
                                outputStreamDeviceDigital.LoadOrder = Convert.ToInt32(measurement.SignalReference.Substring((measurement.SignalReference.LastIndexOf("-") + 3)));
                                outputStreamDeviceDigital.MaskValue = 0;
                                SaveOutputStreamDeviceDigital(outputStreamDeviceDigital, true);
                            }
                        }
                        else
                            SaveOutputStreamMeasurement(outputStreamMeasurement, true);

                    }
                    //********************************************
                }

                return "Output Stream Device(s) Added Successfully";
            }
            finally { }
        }

        #endregion

        #region " Manage Output Stream Device Phasor Code"

        public static List<OutputStreamDevicePhasor> GetOutputStreamDevicePhasorList(int outputStreamDeviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStreamDevicePhasor> outputStreamDevicePhasorList = new List<OutputStreamDevicePhasor>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From OutputStreamDevicePhasor Where OutputStreamDeviceID = @outputStreamDeviceID Order By LoadOrder";
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDeviceID));
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamDevicePhasorList = (from item in resultTable.AsEnumerable()
                                                select new OutputStreamDevicePhasor()
                                                {
                                                    NodeID = item.Field<object>("NodeID").ToString(),
                                                    OutputStreamDeviceID = item.Field<int>("OutputStreamDeviceID"),
                                                    ID = item.Field<int>("ID"),
                                                    Label = item.Field<string>("Label"),
                                                    Type = item.Field<string>("Type"),
                                                    Phase = item.Field<string>("Phase"),
                                                    LoadOrder = item.Field<int>("LoadOrder"),
                                                    ScalingValue = Convert.ToInt32(item.Field<object>("ScalingValue")),
                                                    PhasorType = item.Field<string>("Type") == "V" ? "Voltage" : "Current",
                                                    PhaseType = item.Field<string>("Phase") == "+" ? "Positive" : item.Field<string>("Phase") == "-" ? "Negative" :
                                                                item.Field<string>("Phase") == "A" ? "Phase A" : item.Field<string>("Phase") == "B" ? "Phase B" : "Phase C"
                                                }).ToList();
                return outputStreamDevicePhasorList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveOutputStreamDevicePhasor(OutputStreamDevicePhasor outputStreamDevicePhasor, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into OutputStreamDevicePhasor (NodeID, OutputStreamDeviceID, Label, Type, Phase, LoadOrder, ScalingValue) " +
                        "Values (@nodeID, @outputStreamDeviceID, @label, @type, @phase, @loadOrder, @scalingValue)";
                else
                    command.CommandText = "Update OutputStreamDevicePhasor Set NodeID = @nodeID, OutputStreamDeviceID = @outputStreamDeviceID, Label = @label, " +
                        "Type = @type, Phase = @phase, LoadOrder = @loadOrder, ScalingValue = @scalingValue Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStreamDevicePhasor.NodeID));
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDevicePhasor.OutputStreamDeviceID));
                command.Parameters.Add(AddWithValue(command, "@label", outputStreamDevicePhasor.Label));
                command.Parameters.Add(AddWithValue(command, "@type", outputStreamDevicePhasor.Type));
                command.Parameters.Add(AddWithValue(command, "@phase", outputStreamDevicePhasor.Phase));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", outputStreamDevicePhasor.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@scalingValue", outputStreamDevicePhasor.ScalingValue));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", outputStreamDevicePhasor.ID));

                command.ExecuteNonQuery();
                return "Output Stream Device Phasor Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Output Stream Device Analogs Code"

        public static List<OutputStreamDeviceAnalog> GetOutputStreamDeviceAnalogList(int outputStreamDeviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStreamDeviceAnalog> outputStreamDeviceAnalogList = new List<OutputStreamDeviceAnalog>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From OutputStreamDeviceAnalog Where OutputStreamDeviceID = @outputStreamDeviceID Order By LoadOrder";
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDeviceID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamDeviceAnalogList = (from item in resultTable.AsEnumerable()
                                                select new OutputStreamDeviceAnalog()
                                                {
                                                    NodeID = item.Field<object>("NodeID").ToString(),
                                                    OutputStreamDeviceID = item.Field<int>("OutputStreamDeviceID"),
                                                    ID = item.Field<int>("ID"),
                                                    Label = item.Field<string>("Label"),
                                                    Type = item.Field<int>("Type"),
                                                    LoadOrder = item.Field<int>("LoadOrder"),
                                                    ScalingValue = Convert.ToInt32(item.Field<object>("ScalingValue")),
                                                    TypeName = item.Field<int>("Type") == 0 ? "Single point-on-wave" : item.Field<int>("Type") == 1 ? "RMS of analog input" : "Peak of analog input"
                                                }).ToList();
                return outputStreamDeviceAnalogList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveOutputStreamDeviceAnalog(OutputStreamDeviceAnalog outputStreamDeviceAnalog, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into OutputStreamDeviceAnalog (NodeID, OutputStreamDeviceID, Label, Type, LoadOrder, ScalingValue) " +
                        "Values (@nodeID, @outputStreamDeviceID, @label, @type, @loadOrder, @scalingValue)";
                else
                    command.CommandText = "Update OutputStreamDeviceAnalog Set NodeID = @nodeID, OutputStreamDeviceID = @outputStreamDeviceID, Label = @label, " +
                        "Type = @type, LoadOrder = @loadOrder, ScalingValue = @scalingValue Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStreamDeviceAnalog.NodeID));
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDeviceAnalog.OutputStreamDeviceID));
                command.Parameters.Add(AddWithValue(command, "@label", outputStreamDeviceAnalog.Label));
                command.Parameters.Add(AddWithValue(command, "@type", outputStreamDeviceAnalog.Type));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", outputStreamDeviceAnalog.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@scalingValue", outputStreamDeviceAnalog.ScalingValue));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", outputStreamDeviceAnalog.ID));

                command.ExecuteNonQuery();
                return "Output Stream Device Analog Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Output Stream Device Digitals Code"

        public static List<OutputStreamDeviceDigital> GetOutputStreamDeviceDigitalList(int outputStreamDeviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OutputStreamDeviceDigital> outputStreamDeviceDigitalList = new List<OutputStreamDeviceDigital>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From OutputStreamDeviceDigital Where OutputStreamDeviceID = @outputStreamDeviceID Order By LoadOrder";
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDeviceID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                outputStreamDeviceDigitalList = (from item in resultTable.AsEnumerable()
                                                 select new OutputStreamDeviceDigital()
                                                 {
                                                     NodeID = item.Field<object>("NodeID").ToString(),
                                                     OutputStreamDeviceID = item.Field<int>("OutputStreamDeviceID"),
                                                     ID = item.Field<int>("ID"),
                                                     Label = item.Field<string>("Label"),
                                                     LoadOrder = item.Field<int>("LoadOrder"),
                                                     MaskValue = Convert.ToInt32(item.Field<object>("MaskValue"))
                                                 }).ToList();
                return outputStreamDeviceDigitalList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveOutputStreamDeviceDigital(OutputStreamDeviceDigital outputStreamDeviceDigital, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into OutputStreamDeviceDigital (NodeID, OutputStreamDeviceID, Label, LoadOrder, MaskValue) " +
                        "Values (@nodeID, @outputStreamDeviceID, @label, @loadOrder, @maskValue)";
                else
                    command.CommandText = "Update OutputStreamDeviceDigital Set NodeID = @nodeID, OutputStreamDeviceID = @outputStreamDeviceID, Label = @label, " +
                        "LoadOrder = @loadOrder, MaskValue = @maskValue Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", outputStreamDeviceDigital.NodeID));
                command.Parameters.Add(AddWithValue(command, "@outputStreamDeviceID", outputStreamDeviceDigital.OutputStreamDeviceID));
                command.Parameters.Add(AddWithValue(command, "@label", outputStreamDeviceDigital.Label));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", outputStreamDeviceDigital.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@maskValue", outputStreamDeviceDigital.MaskValue));
                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", outputStreamDeviceDigital.ID));

                command.ExecuteNonQuery();
                return "Output Stream Device Digital Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Historians Code"

        public static List<Historian> GetHistorianList(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Historian> historianList = new List<Historian>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select * From HistorianDetail Order By LoadOrder";
                else
                {
                    command.CommandText = "Select * From HistorianDetail Where NodeID = @nodeID Order By LoadOrder";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                }

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                historianList = (from item in resultTable.AsEnumerable()
                                 select new Historian()
                                 {
                                     NodeID = item.Field<object>("NodeID").ToString(),
                                     ID = item.Field<int>("ID"),
                                     Acronym = item.Field<string>("Acronym"),
                                     Name = item.Field<string>("Name"),
                                     ConnectionString = item.Field<string>("ConnectionString"),
                                     Description = item.Field<string>("Description"),
                                     IsLocal = Convert.ToBoolean(item.Field<object>("IsLocal")),
                                     Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                     LoadOrder = item.Field<int>("LoadOrder"),
                                     TypeName = item.Field<string>("TypeName"),
                                     AssemblyName = item.Field<string>("AssemblyName"),
                                     MeasurementReportingInterval = Convert.ToInt32(item.Field<object>("MeasurementReportingInterval")),
                                     NodeName = item.Field<string>("NodeName")
                                 }).ToList();
                return historianList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static Dictionary<int, string> GetHistorians(bool enabledOnly, bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> historianList = new Dictionary<int, string>();
                if (isOptional)
                    historianList.Add(0, "Select Historian");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (enabledOnly)
                {
                    command.CommandText = "Select ID, Acronym From Historian Where Enabled = @enabled Order By LoadOrder";
                    command.Parameters.Add(AddWithValue(command, "@enabled", true));
                }
                else
                    command.CommandText = "Select ID, Acronym From Historian Order By LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                foreach (DataRow row in resultTable.Rows)
                {
                    if (!historianList.ContainsKey(Convert.ToInt32(row["ID"])))
                        historianList.Add(Convert.ToInt32(row["ID"]), row["Acronym"].ToString());
                }
                return historianList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveHistorian(Historian historian, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (isNew)
                    command.CommandText = "Insert Into Historian (NodeID, Acronym, Name, AssemblyName, TypeName, ConnectionString, IsLocal, MeasurementReportingInterval, Description, LoadOrder, Enabled) Values " +
                        "(@nodeID, @acronym, @name, @assemblyName, @typeName, @connectionString, @isLocal, @measurementReportingInterval, @description, @loadOrder, @enabled)";
                else
                    command.CommandText = "Update Historian Set NodeID = @nodeID, Acronym = @acronym, Name = @name, AssemblyName = @assemblyName, TypeName = @typeName, " +
                        "ConnectionString = @connectionString, IsLocal = @isLocal, MeasurementReportingInterval = @measurementReportingInterval, Description = @description, LoadOrder = @loadOrder, Enabled = @enabled Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", historian.NodeID));
                command.Parameters.Add(AddWithValue(command, "@acronym", historian.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", historian.Name));
                command.Parameters.Add(AddWithValue(command, "@assemblyName", historian.AssemblyName));
                command.Parameters.Add(AddWithValue(command, "@typeName", historian.TypeName));
                command.Parameters.Add(AddWithValue(command, "@connectionString", historian.ConnectionString));
                command.Parameters.Add(AddWithValue(command, "@isLocal", historian.IsLocal));
                command.Parameters.Add(AddWithValue(command, "@measurementReportingInterval", historian.MeasurementReportingInterval));
                command.Parameters.Add(AddWithValue(command, "@description", historian.Description));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", historian.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", historian.Enabled));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", historian.ID));

                command.ExecuteNonQuery();
                return "Historian Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Nodes Code"
        
        public static List<Node> GetNodeList(bool enabledOnly)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Node> nodeList = new List<Node>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (enabledOnly)
                {
                    command.CommandText = "Select * From NodeDetail Where Enabled = @enabled Order By LoadOrder";
                    command.Parameters.Add(AddWithValue(command, "@enabled", true));
                }
                else
                    command.CommandText = "Select * From NodeDetail Order By LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                nodeList = (from item in resultTable.AsEnumerable()
                            select new Node()
                            {
                                ID = item.Field<object>("ID").ToString(),
                                Name = item.Field<string>("Name"),
                                CompanyID = item.Field<int?>("CompanyID"),
                                Longitude = item.Field<decimal?>("Longitude"),
                                Latitude = item.Field<decimal?>("Latitude"),
                                Description = item.Field<string>("Description"),
                                Image = item.Field<string>("ImagePath"),
                                Master = Convert.ToBoolean(item.Field<object>("Master")),
                                LoadOrder = item.Field<int>("LoadOrder"),
                                Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                TimeSeriesDataServiceUrl = item.Field<string>("TimeSeriesDataServiceUrl"),
                                RemoteStatusServiceUrl = item.Field<string>("RemoteStatusServiceUrl"),
                                RealTimeStatisticServiceUrl = item.Field<string>("RealTimeStatisticServiceUrl"),
                                CompanyName = item.Field<string>("CompanyName")
                            }).ToList();
                return nodeList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static Dictionary<string, string> GetNodes(bool enabledOnly, bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<string, string> nodeList = new Dictionary<string, string>();
                if (isOptional)
                    nodeList.Add(string.Empty, "Select Node");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (enabledOnly)
                {
                    command.CommandText = "Select ID, Name From Node Where Enabled = @enabled Order By LoadOrder";
                    command.Parameters.Add(AddWithValue(command, "@enabled", true));
                }
                else
                    command.CommandText = "Select ID, Name From Node Order By LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!nodeList.ContainsKey(row["ID"].ToString()))
                        nodeList.Add(row["ID"].ToString(), row["Name"].ToString());
                }
                return nodeList;
            }            
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static string SaveNode(Node node, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {				
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into Node (Name, CompanyID, Longitude, Latitude, Description, ImagePath, Master, LoadOrder, Enabled, TimeSeriesDataServiceUrl, RemoteStatusServiceUrl, RealTimeStatisticServiceUrl) " +
                        "Values (@name, @companyID, @longitude, @latitude, @description, @image, @master, @loadOrder, @enabled, @timeSeriesDataServiceUrl, @remoteStatusServiceUrl, @realTimeStatisticServiceUrl)";
                else
                    command.CommandText = "Update Node Set Name = @name, CompanyID = @companyID, Longitude = @longitude, Latitude = @latitude, Description = @description, ImagePath = @image, " + 
                        "Master = @master, LoadOrder = @loadOrder, Enabled = @enabled, TimeSeriesDataServiceUrl = @timeSeriesDataServiceUrl, RemoteStatusServiceUrl = @remoteStatusServiceUrl, " +
                        "RealTimeStatisticServiceUrl = @realTimeStatisticServiceUrl Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@name", node.Name));
                command.Parameters.Add(AddWithValue(command, "@companyID", node.CompanyID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@longitude", node.Longitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@latitude", node.Latitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@description", node.Description));
                command.Parameters.Add(AddWithValue(command, "@image", node.Image));
                command.Parameters.Add(AddWithValue(command, "@master", node.Master));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", node.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", node.Enabled));
                command.Parameters.Add(AddWithValue(command, "@timeSeriesDataServiceUrl", node.TimeSeriesDataServiceUrl));
                command.Parameters.Add(AddWithValue(command, "@remoteStatusServiceUrl", node.RemoteStatusServiceUrl));
                command.Parameters.Add(AddWithValue(command, "@realTimeStatisticServiceUrl", node.RealTimeStatisticServiceUrl));
                            
                if (!isNew)
                {
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@id", "{" + node.ID + "}"));						
                    else
                        command.Parameters.Add(AddWithValue(command, "@id", node.ID));
                }

                command.ExecuteNonQuery();
                return "Node Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static Node GetNodeByID(string id)
        {			
            try
            {
                List<Node> nodeList = new List<Node>();
                nodeList = (from item in GetNodeList(false)
                            where item.ID == id
                            select item).ToList();
                if (nodeList.Count > 0)
                    return nodeList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetNodeByID", ex);
                return null;
            }
        }

        #endregion
        
        #region " Manage Vendors Code"
        
        public static List<Vendor> GetVendorList()
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Vendor> vendorList = new List<Vendor>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * FROM VendorDetail Order By Name";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                vendorList = (from item in resultTable.AsEnumerable()
                              select new Vendor()
                              {
                                  ID = item.Field<int>("ID"),
                                  Acronym = item.Field<string>("Acronym"),
                                  Name = item.Field<string>("Name"),
                                  PhoneNumber = item.Field<string>("PhoneNumber"),
                                  ContactEmail = item.Field<string>("ContactEmail"),
                                  URL = item.Field<string>("URL")
                              }).ToList();
                return vendorList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static Dictionary<int, string> GetVendors(bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> vendorList = new Dictionary<int, string>();
                if (isOptional)
                    vendorList.Add(0, "Select Vendor");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Name From Vendor Order By Name";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!vendorList.ContainsKey(Convert.ToInt32(row["ID"])))
                        vendorList.Add(Convert.ToInt32(row["ID"]), row["Name"].ToString());
                }
                return vendorList;
            }
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static string SaveVendor(Vendor vendor, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (isNew)
                    command.CommandText = "Insert Into Vendor (Acronym, Name, PhoneNumber, ContactEmail, URL) Values (@acronym, @name, @phoneNumber, @contactEmail, @url)";
                else
                    command.CommandText = "Update Vendor Set Acronym = @acronym, Name = @name, PhoneNumber = @phoneNumber, ContactEmail = @contactEmail, URL = @url Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@acronym", vendor.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", vendor.Name));
                command.Parameters.Add(AddWithValue(command, "@phoneNumber", vendor.PhoneNumber));
                command.Parameters.Add(AddWithValue(command, "@contactEmail", vendor.ContactEmail));
                command.Parameters.Add(AddWithValue(command, "@url", vendor.URL));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", vendor.ID));

                command.ExecuteNonQuery();
                return "Vendor Information Saved Successfully";
            }
            finally
            {
                connection.Dispose();
            }
        }
        
        #endregion

        #region " Manage Vendor Devices Code"
        
        public static List<VendorDevice> GetVendorDeviceList()
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<VendorDevice> vendorDeviceList = new List<VendorDevice>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From VendorDeviceDetail Order By Name";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                vendorDeviceList = (from item in resultTable.AsEnumerable()
                                    select new VendorDevice()
                                    {
                                        ID = item.Field<int>("ID"),
                                        VendorID = item.Field<int>("VendorID"),
                                        Name = item.Field<string>("Name"),
                                        Description = item.Field<string>("Description"),
                                        URL = item.Field<string>("URL"),
                                        VendorName = item.Field<string>("VendorName")
                                    }).ToList();
                return vendorDeviceList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static string SaveVendorDevice(VendorDevice vendorDevice, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (isNew)
                    command.CommandText = "Insert Into VendorDevice (VendorID, Name, Description, URL) Values (@vendorID, @name, @description, @url)";
                else
                    command.CommandText = "Update VendorDevice Set VendorID = @vendorID, Name = @name, Description = @description, URL = @url Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@vendorID", vendorDevice.VendorID));
                command.Parameters.Add(AddWithValue(command, "@name", vendorDevice.Name));
                command.Parameters.Add(AddWithValue(command, "@description", vendorDevice.Description));
                command.Parameters.Add(AddWithValue(command, "@url", vendorDevice.URL));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", vendorDevice.ID));

                command.ExecuteNonQuery();

                return "Vendor Device Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static Dictionary<int, string> GetVendorDevices(bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> vendorDeviceList = new Dictionary<int, string>();
                if (isOptional)
                    vendorDeviceList.Add(0, "Select Vendor Device");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Name From VendorDevice Order By Name";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!vendorDeviceList.ContainsKey(Convert.ToInt32(row["ID"])))
                        vendorDeviceList.Add(Convert.ToInt32(row["ID"]), row["Name"].ToString());
                }
                return vendorDeviceList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }
        
        #endregion

        #region " Manage Device Code"

        public static List<Device> GetDeviceList(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Device> deviceList = new List<Device>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select * From DeviceDetail Order By Acronym";
                else
                {
                    command.CommandText = "Select * From DeviceDetail Where NodeID = @nodeID Order By Acronym";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                }
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                deviceList = (from item in resultTable.AsEnumerable()
                              select new Device()
                              {
                                  NodeID = item.Field<object>("NodeID").ToString(),
                                  ID = item.Field<int>("ID"),
                                  ParentID = item.Field<int?>("ParentID"),
                                  Acronym = item.Field<string>("Acronym"),
                                  Name = item.Field<string>("Name"),
                                  IsConcentrator = Convert.ToBoolean(item.Field<object>("IsConcentrator")),
                                  CompanyID = item.Field<int?>("CompanyID"),
                                  HistorianID = item.Field<int?>("HistorianID"),
                                  AccessID = item.Field<int>("AccessID"),
                                  VendorDeviceID = item.Field<int?>("VendorDeviceID"),
                                  ProtocolID = item.Field<int?>("ProtocolID"),
                                  Longitude = item.Field<decimal?>("Longitude"),
                                  Latitude = item.Field<decimal?>("Latitude"),
                                  InterconnectionID = item.Field<int?>("InterconnectionID"),
                                  ConnectionString = item.Field<string>("ConnectionString"),
                                  TimeZone = item.Field<string>("TimeZone"),
                                  FramesPerSecond = Convert.ToInt32(item.Field<object>("FramesPerSecond") ?? 30),
                                  TimeAdjustmentTicks = Convert.ToInt64(item.Field<object>("TimeAdjustmentTicks")),
                                  DataLossInterval = item.Field<double>("DataLossInterval"),
                                  ContactList = item.Field<string>("ContactList"),
                                  MeasuredLines = item.Field<int?>("MeasuredLines"),
                                  LoadOrder = item.Field<int>("LoadOrder"),
                                  Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                  CreatedOn = item.Field<DateTime>("CreatedOn"),
                                  AllowedParsingExceptions = Convert.ToInt32(item.Field<object>("AllowedParsingExceptions")),
                                  ParsingExceptionWindow = item.Field<double>("ParsingExceptionWindow"),
                                  DelayedConnectionInterval = item.Field<double>("DelayedConnectionInterval"),
                                  AllowUseOfCachedConfiguration = Convert.ToBoolean(item.Field<object>("AllowUseOfCachedConfiguration")),
                                  AutoStartDataParsingSequence = Convert.ToBoolean(item.Field<object>("AutoStartDataParsingSequence")),
                                  SkipDisableRealTimeData = Convert.ToBoolean(item.Field<object>("SkipDisableRealTimeData")),
                                  MeasurementReportingInterval = Convert.ToInt32(item.Field<object>("MeasurementReportingInterval")),
                                  CompanyName = item.Field<string>("CompanyName"),
                                  CompanyAcronym = item.Field<string>("CompanyAcronym"),
                                  HistorianAcronym = item.Field<string>("HistorianAcronym"),
                                  VendorDeviceName = item.Field<string>("VendorDeviceName"),
                                  VendorAcronym = item.Field<string>("VendorAcronym"),
                                  ProtocolName = item.Field<string>("ProtocolName"),
                                  InterconnectionName = item.Field<string>("InterconnectionName"),
                                  NodeName = item.Field<string>("NodeName"),
                                  ParentAcronym = item.Field<string>("ParentAcronym")
                              }).ToList();
                return deviceList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static List<Device> GetDeviceListByParentID(int parentID)
        {
            List<Device> deviceList = new List<Device>();
            try
            {				
                deviceList = (from item in GetDeviceList(string.Empty)
                              where item.ParentID == parentID
                              select item).ToList();				
            }
            catch (Exception ex)
            {
                LogException("GetDeviceListByParentID", ex);
            }
            return deviceList;
        }

        public static string SaveDevice(Device device, bool isNew, int digitalCount, int analogCount)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();

                if (isNew)
                    command.CommandText = "Insert Into Device (NodeID, ParentID, Acronym, Name, IsConcentrator, CompanyID, HistorianID, AccessID, VendorDeviceID, ProtocolID, Longitude, Latitude, InterconnectionID, ConnectionString, TimeZone, FramesPerSecond, TimeAdjustmentTicks, " +
                        "DataLossInterval, ContactList, MeasuredLines, LoadOrder, Enabled, AllowedParsingExceptions, ParsingExceptionWindow, DelayedConnectionInterval, AllowUseOfCachedConfiguration, AutoStartDataParsingSequence, SkipDisableRealTimeData, MeasurementReportingInterval) " +
                        "Values (@nodeID, @parentID, @acronym, @name, @isConcentrator, @companyID, @historianID, @accessID, @vendorDeviceID, @protocolID, @longitude, @latitude, @interconnectionID, " +
                        "@connectionString, @timezone, @framesPerSecond, @timeAdjustmentTicks, @dataLossInterval, @contactList, @measuredLines, @loadOrder, @enabled, @allowedParsingExceptions, " + 
                        "@parsingExceptionWindow, @delayedConnectionInterval, @allowUseOfCachedConfiguration, @autoStartDataParsingSequence, @skipDisableRealTimeData, @measurementReportingInterval)";
                else
                    command.CommandText = "Update Device Set NodeID = @nodeID, ParentID = @parentID, Acronym = @acronym, Name = @name, IsConcentrator = @isConcentrator, CompanyID = @companyID, HistorianID = @historianID, AccessID = @accessID, VendorDeviceID = @vendorDeviceID, " +
                        "ProtocolID = @protocolID, Longitude = @longitude, Latitude = @latitude, InterconnectionID = @interconnectionID, ConnectionString = @connectionString, TimeZone = @timezone, FramesPerSecond = @framesPerSecond, TimeAdjustmentTicks = @timeAdjustmentTicks, DataLossInterval = @dataLossInterval, " +
                        "ContactList = @contactList, MeasuredLines = @measuredLines, LoadOrder = @loadOrder, Enabled = @enabled, AllowedParsingExceptions = @allowedParsingExceptions, ParsingExceptionWindow = @parsingExceptionWindow, DelayedConnectionInterval = @delayedConnectionInterval, " + 
                        "AllowUseOfCachedConfiguration = @allowUseOfCachedConfiguration, AutoStartDataParsingSequence = @autoStartDataParsingSequence, SkipDisableRealTimeData = @skipDisableRealTimeData, MeasurementReportingInterval = @measurementReportingInterval WHERE ID = @id";

                command.CommandType = CommandType.Text;
                command.Parameters.Add(AddWithValue(command, "@nodeID", device.NodeID));
                command.Parameters.Add(AddWithValue(command, "@parentID", device.ParentID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@acronym", device.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", device.Name));
                command.Parameters.Add(AddWithValue(command, "@isConcentrator", device.IsConcentrator));
                command.Parameters.Add(AddWithValue(command, "@companyID", device.CompanyID));
                command.Parameters.Add(AddWithValue(command, "@historianID", device.HistorianID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@accessID", device.AccessID));
                command.Parameters.Add(AddWithValue(command, "@vendorDeviceID", device.VendorDeviceID == null ? (object)DBNull.Value : device.VendorDeviceID == 0 ? (object)DBNull.Value : device.VendorDeviceID));
                command.Parameters.Add(AddWithValue(command, "@protocolID", device.ProtocolID));
                command.Parameters.Add(AddWithValue(command, "@longitude", device.Longitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@latitude", device.Latitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@interconnectionID", device.InterconnectionID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@connectionString", device.ConnectionString));
                command.Parameters.Add(AddWithValue(command, "@timezone", device.TimeZone));
                command.Parameters.Add(AddWithValue(command, "@framesPerSecond", device.FramesPerSecond ?? 30));
                command.Parameters.Add(AddWithValue(command, "@timeAdjustmentTicks", device.TimeAdjustmentTicks));
                command.Parameters.Add(AddWithValue(command, "@dataLossInterval", device.DataLossInterval));
                command.Parameters.Add(AddWithValue(command, "@contactList", device.ContactList));
                command.Parameters.Add(AddWithValue(command, "@measuredLines", device.MeasuredLines ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", device.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", device.Enabled));
                command.Parameters.Add(AddWithValue(command, "@allowedParsingExceptions", device.AllowedParsingExceptions));
                command.Parameters.Add(AddWithValue(command, "@parsingExceptionWindow", device.ParsingExceptionWindow));
                command.Parameters.Add(AddWithValue(command, "@delayedConnectionInterval", device.DelayedConnectionInterval));
                command.Parameters.Add(AddWithValue(command, "@allowUseOfCachedConfiguration", device.AllowUseOfCachedConfiguration));
                command.Parameters.Add(AddWithValue(command, "@autoStartDataParsingSequence", device.AutoStartDataParsingSequence));
                command.Parameters.Add(AddWithValue(command, "@skipDisableRealTimeData", device.SkipDisableRealTimeData));
                command.Parameters.Add(AddWithValue(command, "@measurementReportingInterval", device.MeasurementReportingInterval));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", device.ID));

                command.ExecuteNonQuery();

                if (device.IsConcentrator)
                    return "Concentrator Device Information Saved Successfully";		//As we do not add measurements for PDC device or device which is concentrator.

                DataTable pmuSignalTypes = new DataTable();
                pmuSignalTypes = GetPmuSignalTypes();

                Measurement measurement;				

                Device addedDevice = new Device();
                addedDevice = GetDeviceByAcronym(device.Acronym);

                //We will try again in a while if addedDevice is NULL. This is done because MS Access is very slow and was returning NULL.
                if (addedDevice == null)
                {
                    System.Threading.Thread.Sleep(500);
                    addedDevice = GetDeviceByAcronym(device.Acronym);
                }
                            
                foreach (DataRow row in pmuSignalTypes.Rows)	//This will only create or update PMU related measurements and not phasor related.
                {
                    measurement = new Measurement();
                    measurement.HistorianID = addedDevice.HistorianID;
                    measurement.DeviceID = addedDevice.ID;
                    measurement.PointTag = addedDevice.CompanyAcronym + "_" + addedDevice.Acronym + ":" + addedDevice.VendorAcronym + row["Abbreviation"].ToString();
                    measurement.AlternateTag = string.Empty;
                    measurement.SignalTypeID = Convert.ToInt32(row["ID"]);
                    measurement.PhasorSourceIndex = (int?)null;
                    measurement.SignalReference = addedDevice.Acronym + "-" + row["Suffix"].ToString();
                    measurement.Adder = 0.0d;
                    measurement.Multiplier = 1.0d;
                    measurement.Description = addedDevice.Name + " " + addedDevice.VendorDeviceName + " " + row["Name"].ToString();
                    measurement.Enabled = true;
                    if (isNew)	//if it is a new device then measurements are new too. So don't worry about updating them.
                        SaveMeasurement(measurement, true);
                    else	//if device is existing one, then check and see if its measusremnts exist, if so then update measurements.
                    {
                        Measurement existingMeasurement = new Measurement();
                        existingMeasurement = GetMeasurementInfo(measurement.DeviceID, row["Suffix"].ToString(), measurement.PhasorSourceIndex);

                        if (existingMeasurement == null)	//measurement does not exist for this device and signal type then add as a new measurement otherwise update.
                            SaveMeasurement(measurement, true);
                        else
                        {
                            measurement.SignalID = existingMeasurement.SignalID;
                            SaveMeasurement(measurement, false);
                        }
                    }
                }

                if (digitalCount > 0)
                {
                    for (int i = 1; i <= digitalCount; i++)
                    {
                        measurement = new Measurement();
                        measurement.HistorianID = addedDevice.HistorianID;
                        measurement.DeviceID = addedDevice.ID;
                        measurement.PointTag = addedDevice.CompanyAcronym + "_" + addedDevice.Acronym + ":" + addedDevice.VendorAcronym + "D" + i.ToString();
                        measurement.AlternateTag = string.Empty;
                        measurement.SignalTypeID = GetSignalTypeID("DV");
                        measurement.PhasorSourceIndex = (int?)null;
                        measurement.SignalReference = addedDevice.Acronym + "-DV" + i.ToString();
                        measurement.Adder = 0.0d;
                        measurement.Multiplier = 1.0d;
                        measurement.Description = addedDevice.Name + " " + addedDevice.VendorDeviceName + " Digital Value " + i.ToString();
                        measurement.Enabled = true;
                        if (isNew)	//if it is a new device then measurements are new too. So don't worry about updating them.
                            SaveMeasurement(measurement, true);
                        else	//if device is existing one, then check and see if its measusremnts exist, if so then update measurements.
                        {
                            Measurement existingMeasurement = new Measurement();
                            //we will compare using signal reference as signal suffix doesn't provide uniqueness.
                            existingMeasurement = GetMeasurementInfoBySignalReference(measurement.DeviceID, measurement.SignalReference, measurement.PhasorSourceIndex);

                            if (existingMeasurement == null)	//measurement does not exist for this device and signal type then add as a new measurement otherwise update.
                                SaveMeasurement(measurement, true);
                            else
                            {
                                measurement.SignalID = existingMeasurement.SignalID;
                                SaveMeasurement(measurement, false);
                            }
                        }
                    }
                }

                if (analogCount > 0)
                {
                    for (int i = 1; i <= analogCount; i++)
                    {
                        measurement = new Measurement();
                        measurement.HistorianID = addedDevice.HistorianID;
                        measurement.DeviceID = addedDevice.ID;
                        measurement.PointTag = addedDevice.CompanyAcronym + "_" + addedDevice.Acronym + ":" + addedDevice.VendorAcronym + "A" + i.ToString();
                        measurement.AlternateTag = string.Empty;
                        measurement.SignalTypeID = GetSignalTypeID("AV");
                        measurement.PhasorSourceIndex = (int?)null;
                        measurement.SignalReference = addedDevice.Acronym + "-AV" + i.ToString();
                        measurement.Adder = 0.0d;
                        measurement.Multiplier = 1.0d;
                        measurement.Description = addedDevice.Name + " " + addedDevice.VendorDeviceName + " Analog Value " + i.ToString();
                        measurement.Enabled = true;
                        if (isNew)	//if it is a new device then measurements are new too. So don't worry about updating them.
                            SaveMeasurement(measurement, true);
                        else	//if device is existing one, then check and see if its measusremnts exist, if so then update measurements.
                        {
                            Measurement existingMeasurement = new Measurement();
                            existingMeasurement = GetMeasurementInfoBySignalReference(measurement.DeviceID, measurement.SignalReference, measurement.PhasorSourceIndex);

                            if (existingMeasurement == null)	//measurement does not exist for this device and signal type then add as a new measurement otherwise update.
                                SaveMeasurement(measurement, true);
                            else
                            {
                                measurement.SignalID = existingMeasurement.SignalID;
                                SaveMeasurement(measurement, false);
                            }
                        }
                    }
                }

                if (!isNew)
                {
                    //After all the PMU related measurements are updated then lets go through each phasors for the PMU
                    //and update all the phasors and their measurements to reflect changes made to the PMU configuration.
                    //We are not going to make any changes to the Phasor definition itselft but only to reflect PMU related
                    //changes in the measurement.

                    foreach (Phasor phasor in GetPhasorList(addedDevice.ID))
                    {
                        SavePhasor(phasor, false);	//we will save phasor without modifying it so that only measurements will reflect changes related to PMU.
                        //nothing will change in phasor itself.
                    }
                }

                return "Device Information Saved Successfully";
            }
            
            finally
            {
                connection.Dispose();
            }
        }

        public static Dictionary<int, string> GetDevices(DeviceType deviceType, string nodeID, bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> deviceList = new Dictionary<int, string>();
                if (!string.IsNullOrEmpty(nodeID))
                {
                    //throw new ArgumentException("Invalid value of NodeID.");
                   
                    if (isOptional)
                        deviceList.Add(0, "Select Device");

                    IDbCommand command = connection.Connection.CreateCommand();
                    command.CommandType = CommandType.Text;
                    if (deviceType == DeviceType.Concentrator)
                    {
                        command.CommandText = "Select ID, Acronym From Device Where IsConcentrator = @isConcentrator AND NodeID = @nodeID Order By LoadOrder";
                        command.Parameters.Add(AddWithValue(command, "@isConcentrator", true));
                    }
                    else if (deviceType == DeviceType.NonConcentrator)
                    {
                        command.CommandText = "Select ID, Acronym From Device Where IsConcentrator = @isConcentrator AND NodeID = @nodeID Order By LoadOrder";
                        command.Parameters.Add(AddWithValue(command, "@isConcentrator", false));
                    }
                    else
                        command.CommandText = "Select ID, Acronym From Device Where NodeID = @nodeID Order By LoadOrder";


                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));

                    DataTable resultTable = new DataTable();
                    resultTable.Load(command.ExecuteReader());

                    foreach (DataRow row in resultTable.Rows)
                    {
                        if (!deviceList.ContainsKey(Convert.ToInt32(row["ID"])))
                            deviceList.Add(Convert.ToInt32(row["ID"]), row["Acronym"].ToString());
                    }                    
                }
                return deviceList;
            }            
            finally
            {
                connection.Dispose();
            }			
        }

        public static Device GetDeviceByDeviceID(int deviceID)
        {
            try
            {
                List<Device> deviceList = new List<Device>();
                deviceList = (from item in GetDeviceList(string.Empty)
                              where item.ID == deviceID
                              select item).ToList();
                if (deviceList.Count > 0)
                    return deviceList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetDeviceByDeviceID", ex);
                return null;
            }
        }

        public static Device GetDeviceByAcronym(string acronym)
        {
            try
            {
                List<Device> deviceList = new List<Device>();
                deviceList = (from item in GetDeviceList(string.Empty)
                              where item.Acronym == acronym
                              select item).ToList();
                if (deviceList.Count > 0)
                    return deviceList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetDeviceByAcronym", ex);
                return null;
            }
        }

        public static Dictionary<int, string> GetDevicesForOutputStream(int outputStreamID, string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> deviceList = new Dictionary<int, string>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Acronym From Device Where NodeID = @nodeID AND IsConcentrator = @isConcentrator AND Acronym NOT IN (Select Acronym From OutputStreamDevice Where AdapterID = @adapterID)";				
                command.Parameters.Add(AddWithValue(command, "@adapterID", outputStreamID));	//this has to be the first paramter for MS Access to succeed because it evaluates subquery first.
                //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                else
                    command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                command.Parameters.Add(AddWithValue(command, "@isConcentrator", false));
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!deviceList.ContainsKey(Convert.ToInt32(row["ID"])))
                        deviceList.Add(Convert.ToInt32(row["ID"]), row["Acronym"].ToString());
                }
                return deviceList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static Device GetConcentratorDevice(int deviceID)
        {
            try
            {
                Device device = new Device();
                device = GetDeviceByDeviceID(deviceID);
                if (device.IsConcentrator)
                    return device;
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetConcentratorDevice", ex);
                return null;
            }
        }

        public static string DeleteDevice(int deviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Device device = GetDeviceByDeviceID(deviceID);
                string deviceAcronym = device.Acronym;
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Delete From Device Where ID = @id";
                command.Parameters.Add(AddWithValue(command, "@id", deviceID));
                command.ExecuteNonQuery();

                if (!string.IsNullOrEmpty(deviceAcronym))
                {
                    command = connection.Connection.CreateCommand();
                    command.CommandType = CommandType.Text;
                    //we will delete device from output stream if a device is in the same node.
                    command.CommandText = "Delete From OutputStreamDevice Where Acronym = @acronym AND NodeID = @nodeID";
                    command.Parameters.Add(AddWithValue(command, "@acronym", deviceAcronym));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + device.NodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", device.NodeID));
                    command.ExecuteNonQuery();
                }

                return "Device Deleted Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Phasor Code"

        public static List<Phasor> GetPhasorList(int deviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Phasor> phasorList = new List<Phasor>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From PhasorDetail Where DeviceID = @deviceID Order By SourceIndex";
                command.Parameters.Add(AddWithValue(command, "@deviceID", deviceID));

                phasorList = (from item in GetResultSet(command).Tables[0].AsEnumerable()
                              select new Phasor()
                              {
                                  ID = item.Field<int>("ID"),
                                  DeviceID = item.Field<int>("DeviceID"),
                                  Label = item.Field<string>("Label"),
                                  Type = item.Field<string>("Type"),
                                  Phase = item.Field<string>("Phase"),
                                  DestinationPhasorID = item.Field<int?>("DestinationPhasorID"),
                                  SourceIndex = item.Field<int>("SourceIndex"),
                                  DestinationPhasorLabel = item.Field<string>("DestinationPhasorLabel"),
                                  DeviceAcronym = item.Field<string>("DeviceAcronym"),
                                  PhasorType = item.Field<string>("Type") == "V" ? "Voltage" : "Current",
                                  PhaseType = item.Field<string>("Phase") == "+" ? "Positive" : item.Field<string>("Phase") == "-" ? "Negative" :
                                              item.Field<string>("Phase") == "A" ? "Phase A" : item.Field<string>("Phase") == "B" ? "Phase B" : "Phase C"
                              }).ToList();
                return phasorList;
            }			
            finally
            {
                connection.Dispose();
            }						
        }

        public static Dictionary<int, string> GetPhasors(int deviceID, bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> phasorList = new Dictionary<int, string>();
                if (isOptional)
                    phasorList.Add(0, "Select Phasor");
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Label From Phasor Where DeviceID = @deviceID Order By SourceIndex";
                command.Parameters.Add(AddWithValue(command, "@deviceID", deviceID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!phasorList.ContainsKey(Convert.ToInt32(row["ID"])))
                        phasorList.Add(Convert.ToInt32(row["ID"]), row["Label"].ToString());
                }
                return phasorList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string SavePhasor(Phasor phasor, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();

                if (isNew)
                    command.CommandText = "Insert Into Phasor (DeviceID, Label, Type, Phase, DestinationPhasorID, SourceIndex) Values (@deviceID, @label, @type, @phase, " +
                        "@destinationPhasorID, @sourceIndex)";
                else
                    command.CommandText = "Update Phasor Set DeviceID =@deviceID, Label = @label, Type = @type, Phase = @phase, DestinationPhasorID = @destinationPhasorID, " +
                        "SourceIndex = @sourceIndex Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@deviceID", phasor.DeviceID));
                command.Parameters.Add(AddWithValue(command, "@label", phasor.Label));
                command.Parameters.Add(AddWithValue(command, "@type", phasor.Type));
                command.Parameters.Add(AddWithValue(command, "@phase", phasor.Phase));
                command.Parameters.Add(AddWithValue(command, "@destinationPhasorID", phasor.DestinationPhasorID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@sourceIndex", phasor.SourceIndex));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", phasor.ID));

                command.ExecuteNonQuery();							

                Device device = new Device();
                device = GetDeviceByDeviceID(phasor.DeviceID);

                Measurement measurement;

                DataTable phasorSignalTypes = new DataTable();
                phasorSignalTypes = GetPhasorSignalTypes(phasor.Type);
                
                Phasor addedPhasor = new Phasor();				
                //addedPhasor = GetPhasorByLabel(phasor.DeviceID, phasor.Label);
                addedPhasor = GetPhasorBySourceIndex(phasor.DeviceID, phasor.SourceIndex);

                //we will try again just to make sure we get information back about the added phasor. As MS Access is very slow and sometimes fails to retrieve data.
                if (addedPhasor == null)
                {
                    System.Threading.Thread.Sleep(500);
                    //addedPhasor = GetPhasorByLabel(phasor.DeviceID, phasor.Label);
                    addedPhasor = GetPhasorBySourceIndex(phasor.DeviceID, phasor.SourceIndex);
                }

                foreach (DataRow row in phasorSignalTypes.Rows)
                {
                    measurement = new Measurement();
                    measurement.HistorianID = device.HistorianID;
                    measurement.DeviceID = device.ID;
                    if (addedPhasor.DestinationPhasorID.HasValue)
                        measurement.PointTag = device.CompanyAcronym + "_" + device.Acronym + "-" + GetPhasorByID(addedPhasor.DeviceID, (int)addedPhasor.DestinationPhasorID).Label + ":" + device.VendorAcronym + row["Abbreviation"].ToString();
                    else
                        measurement.PointTag = device.CompanyAcronym + "_" + device.Acronym + "-" + row["Suffix"].ToString() + addedPhasor.SourceIndex.ToString() + ":" + device.VendorAcronym + row["Abbreviation"].ToString();
                    measurement.AlternateTag = string.Empty;
                    measurement.SignalTypeID = Convert.ToInt32(row["ID"]);
                    measurement.PhasorSourceIndex = addedPhasor.SourceIndex;
                    measurement.SignalReference = device.Acronym + "-" + row["Suffix"].ToString() + addedPhasor.SourceIndex.ToString();
                    measurement.Adder = 0.0d;
                    measurement.Multiplier = 1.0d;
                    measurement.Description = device.Name + " " + addedPhasor.Label + " " + device.VendorDeviceName + " " + addedPhasor.PhaseType + " " + row["Name"].ToString();
                    measurement.Enabled = true;
                    if (isNew)	//if it is a new phasor then add measurements as new.
                        SaveMeasurement(measurement, true);
                    else //Check if measurement exists, if so then update them otherwise add new.
                    {
                        Measurement existingMeasurement = new Measurement();
                        existingMeasurement = GetMeasurementInfo(measurement.DeviceID, row["Suffix"].ToString(), measurement.PhasorSourceIndex);
                        if (existingMeasurement == null)
                            SaveMeasurement(measurement, true);
                        else
                        {
                            measurement.SignalID = existingMeasurement.SignalID;
                            SaveMeasurement(measurement, false);
                        }
                    }
                }

                return "Phasor Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        static Phasor GetPhasorByLabel(int deviceID, string label)
        {
            try
            {
                List<Phasor> phasorList = new List<Phasor>();
                phasorList = (from item in GetPhasorList(deviceID)
                              where item.Label == label
                              select item).ToList();
                if (phasorList.Count > 0)
                    return phasorList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetPhasorByLabel", ex);
                return null;
            }
        }

        static Phasor GetPhasorByID(int deviceID, int phasorID)
        {
            try
            {
                List<Phasor> phasorList = new List<Phasor>();
                phasorList = (from item in GetPhasorList(deviceID)
                              where item.ID == phasorID
                              select item).ToList();
                if (phasorList.Count > 0)
                    return phasorList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetPhasorByID", ex);
                return null;
            }
        }

        static Phasor GetPhasorBySourceIndex(int deviceID, int sourceIndex)
        {
            try
            {
                List<Phasor> phasorList = new List<Phasor>();
                phasorList = (from item in GetPhasorList(deviceID)
                              where item.SourceIndex == sourceIndex
                              select item).ToList();
                if (phasorList.Count > 0)
                    return phasorList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetPhasorBySourceIndex", ex);
                return null;
            }
        }

        #endregion

        #region " Manage Measurements Code"

        public static List<Measurement> GetMeasurementList(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select SignalID, HistorianID, PointID, DeviceID, PointTag, AlternateTag, SignalTypeID, PhasorSourceIndex, SignalReference, " +
                        "Adder, Multiplier, Description, Enabled, HistorianAcronym, DeviceAcronym, SignalName, SignalAcronym, SignalTypeSuffix, PhasorLabel From MeasurementDetail Order By PointTag";
                else
                {
                    command.CommandText = "Select SignalID, HistorianID, PointID, DeviceID, PointTag, AlternateTag, SignalTypeID, PhasorSourceIndex, SignalReference, " +
                        "Adder, Multiplier, Description, Enabled, HistorianAcronym, DeviceAcronym, SignalName, SignalAcronym, SignalTypeSuffix, PhasorLabel From MeasurementDetail Where NodeID = @nodeID Order By PointTag";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));

                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));


                }
                
                measurementList = (from item in GetResultSet(command).Tables[0].AsEnumerable()
                                   select new Measurement()
                                   {
                                       SignalID = item.Field<object>("SignalID").ToString(),
                                       HistorianID = item.Field<int?>("HistorianID"),
                                       PointID = item.Field<int>("PointID"),
                                       DeviceID = item.Field<int?>("DeviceID"),
                                       PointTag = item.Field<string>("PointTag"),
                                       AlternateTag = item.Field<string>("AlternateTag"),
                                       SignalTypeID = item.Field<int>("SignalTypeID"),
                                       PhasorSourceIndex = item.Field<int?>("PhasorSourceIndex"),
                                       SignalReference = item.Field<string>("SignalReference"),
                                       Adder = item.Field<double>("Adder"),
                                       Multiplier = item.Field<double>("Multiplier"),
                                       Description = item.Field<string>("Description"),
                                       Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                       HistorianAcronym = item.Field<string>("HistorianAcronym"),
                                       DeviceAcronym = item.Field<string>("DeviceAcronym"),
                                       SignalName = item.Field<string>("SignalName"),
                                       SignalAcronym = item.Field<string>("SignalAcronym"),
                                       SignalSuffix = item.Field<string>("SignalTypeSuffix"),
                                       PhasorLabel = item.Field<string>("PhasorLabel")
                                   }).ToList();
                return measurementList;
            }			
            finally
            {
                connection.Dispose();
            }						
        }

        public static List<Measurement> GetMeasurementsByDevice(int deviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                IDbCommand commnad = connection.Connection.CreateCommand();
                commnad.CommandType = CommandType.Text;
                commnad.CommandText = "Select * From MeasurementDetail Where DeviceID = @deviceID Order By PointTag";
                commnad.Parameters.Add(AddWithValue(commnad, "@deviceID", deviceID));

                measurementList = (from item in GetResultSet(commnad).Tables[0].AsEnumerable()
                                   select new Measurement()
                                   {
                                       SignalID = item.Field<object>("SignalID").ToString(),
                                       HistorianID = item.Field<int?>("HistorianID"),
                                       PointID = item.Field<int>("PointID"),
                                       DeviceID = item.Field<int>("DeviceID"),
                                       PointTag = item.Field<string>("PointTag"),
                                       AlternateTag = item.Field<string>("AlternateTag"),
                                       SignalTypeID = item.Field<int>("SignalTypeID"),
                                       PhasorSourceIndex = item.Field<int?>("PhasorSourceIndex"),
                                       SignalReference = item.Field<string>("SignalReference"),
                                       Adder = item.Field<double>("Adder"),
                                       Multiplier = item.Field<double>("Multiplier"),
                                       Description = item.Field<string>("Description"),
                                       Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                       HistorianAcronym = item.Field<string>("HistorianAcronym"),
                                       DeviceAcronym = item.Field<string>("DeviceAcronym"),
                                       SignalName = item.Field<string>("SignalName"),
                                       SignalAcronym = item.Field<string>("SignalAcronym"),
                                       SignalSuffix = item.Field<string>("SignalTypeSuffix"),
                                       PhasorLabel = item.Field<string>("PhasorLabel")
                                   }).ToList();
                return measurementList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }
        
        public static string SaveMeasurement(Measurement measurement, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();

                if (isNew)
                    command.CommandText = "Insert Into Measurement (HistorianID, DeviceID, PointTag, AlternateTag, SignalTypeID, PhasorSourceIndex, SignalReference, Adder, Multiplier, Description, Enabled) " +
                        "Values (@historianID, @deviceID, @pointTag, @alternateTag, @signalTypeID, @phasorSourceIndex, @signalReference, @adder, @multiplier, @description, @enabled)";
                else
                    command.CommandText = "Update Measurement Set HistorianID = @historianID, DeviceID = @deviceID, PointTag = @pointTag, AlternateTag = @alternateTag, SignalTypeID = @signalTypeID, " +
                        "PhasorSourceIndex = @phasorSourceIndex, SignalReference = @signalReference, Adder = @adder, Multiplier = @multiplier, Description = @description, Enabled = @enabled Where SignalID = @signalID";

                command.Parameters.Add(AddWithValue(command, "@historianID", measurement.HistorianID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@deviceID", measurement.DeviceID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@pointTag", measurement.PointTag));
                command.Parameters.Add(AddWithValue(command, "@alternateTag", measurement.AlternateTag));
                command.Parameters.Add(AddWithValue(command, "@signalTypeID", measurement.SignalTypeID));
                command.Parameters.Add(AddWithValue(command, "@phasorSourceIndex", measurement.PhasorSourceIndex ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@signalReference", measurement.SignalReference));
                command.Parameters.Add(AddWithValue(command, "@adder", measurement.Adder));
                command.Parameters.Add(AddWithValue(command, "@multiplier", measurement.Multiplier));
                command.Parameters.Add(AddWithValue(command, "@description", measurement.Description));
                command.Parameters.Add(AddWithValue(command, "@enabled", measurement.Enabled));							

                if (!isNew)
                {
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@signalID", "{" + measurement.SignalID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@signalID", measurement.SignalID));
                }

                command.ExecuteNonQuery();
                return "Measurement Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static List<Measurement> GetMeasurementsForOutputStream(string nodeID, int outputStreamID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select * From MeasurementDetail Where PointID Not In (Select PointID From OutputStreamMeasurement Where AdapterID = @outputStreamID)";
                else
                {
                    command.CommandText = "Select * From MeasurementDetail Where NodeID = @nodeID AND PointID Not In (Select PointID From OutputStreamMeasurement Where AdapterID = @outputStreamID)";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                }
                command.Parameters.Add(AddWithValue(command, "@outputStreamID", outputStreamID));

                measurementList = (from item in GetResultSet(command).Tables[0].AsEnumerable()
                                   select new Measurement()
                                   {
                                       SignalID = item.Field<object>("SignalID").ToString(),
                                       HistorianID = item.Field<int?>("HistorianID"),
                                       PointID = item.Field<int>("PointID"),
                                       DeviceID = item.Field<int?>("DeviceID"),
                                       PointTag = item.Field<string>("PointTag"),
                                       AlternateTag = item.Field<string>("AlternateTag"),
                                       SignalTypeID = item.Field<int>("SignalTypeID"),
                                       PhasorSourceIndex = item.Field<int?>("PhasorSourceIndex"),
                                       SignalReference = item.Field<string>("SignalReference"),
                                       Adder = item.Field<double>("Adder"),
                                       Multiplier = item.Field<double>("Multiplier"),
                                       Description = item.Field<string>("Description"),
                                       Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                       HistorianAcronym = item.Field<string>("HistorianAcronym"),
                                       DeviceAcronym = item.Field<string>("DeviceAcronym"),
                                       SignalName = item.Field<string>("SignalName"),
                                       SignalAcronym = item.Field<string>("SignalAcronym"),
                                       SignalSuffix = item.Field<string>("SignalTypeSuffix"),
                                       PhasorLabel = item.Field<string>("PhasorLabel")
                                   }).ToList();				
                return measurementList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static Measurement GetMeasurementInfo(int? deviceID, string signalSuffix, int? phasorSourceIndex)	//we are using signal suffix because it remains same if phasor is current or voltage which helps in modifying existing measurement if phasor changes from voltage to current.
        {
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                measurementList = (from item in GetMeasurementsByDevice((int)deviceID)
                                   where item.SignalSuffix == signalSuffix && item.PhasorSourceIndex == phasorSourceIndex
                                   select item).ToList();
                if (measurementList.Count > 0)
                    return measurementList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetMeasurementsForOutputStream", ex);
                return null;
            }
        }		
        
        private static Measurement GetMeasurementInfoBySignalReference(int? deviceID, string signalReference, int? phasorSourceIndex)
        {
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                measurementList = (from item in GetMeasurementsByDevice((int)deviceID)
                                   where item.SignalReference == signalReference && item.PhasorSourceIndex == phasorSourceIndex
                                   select item).ToList();
                if (measurementList.Count > 0)
                    return measurementList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetMeasurementsForOutputStream", ex);
                return null;
            }
        }

        public static List<Measurement> GetFilteredMeasurementsByDevice(int deviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Measurement> measurementList = new List<Measurement>();
                IDbCommand commnad = connection.Connection.CreateCommand();
                commnad.CommandType = CommandType.Text;
                commnad.CommandText = "Select * From MeasurementDetail Where DeviceID = @deviceID AND SignalTypeSuffix IN ('PA', 'FQ') Order By PointTag";
                commnad.Parameters.Add(AddWithValue(commnad, "@deviceID", deviceID));

                measurementList = (from item in GetResultSet(commnad).Tables[0].AsEnumerable()
                                   select new Measurement()
                                   {
                                       SignalID = item.Field<object>("SignalID").ToString(),
                                       HistorianID = item.Field<int?>("HistorianID"),
                                       PointID = item.Field<int>("PointID"),
                                       DeviceID = item.Field<int>("DeviceID"),
                                       PointTag = item.Field<string>("PointTag"),
                                       AlternateTag = item.Field<string>("AlternateTag"),
                                       SignalTypeID = item.Field<int>("SignalTypeID"),
                                       PhasorSourceIndex = item.Field<int?>("PhasorSourceIndex"),
                                       SignalReference = item.Field<string>("SignalReference"),
                                       Adder = item.Field<double>("Adder"),
                                       Multiplier = item.Field<double>("Multiplier"),
                                       Description = item.Field<string>("Description"),
                                       Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                       HistorianAcronym = item.Field<string>("HistorianAcronym"),
                                       DeviceAcronym = item.Field<string>("DeviceAcronym"),
                                       FramesPerSecond = Convert.ToInt32(item.Field<object>("FramesPerSecond") ?? 30),
                                       SignalName = item.Field<string>("SignalName"),
                                       SignalAcronym = item.Field<string>("SignalAcronym"),
                                       SignalSuffix = item.Field<string>("SignalTypeSuffix"),
                                       PhasorLabel = item.Field<string>("PhasorLabel")
                                   }).ToList();
                return measurementList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        #endregion

        #region " Manage Other Devices"

        public static List<OtherDevice> GetOtherDeviceList()
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<OtherDevice> otherDeviceList = new List<OtherDevice>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From OtherDeviceDetail Order By Acronym, Name";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                otherDeviceList = (from item in resultTable.AsEnumerable()
                                   select new OtherDevice()
                                   {
                                       ID = item.Field<int>("ID"),
                                       Acronym = item.Field<string>("Acronym"),
                                       Name = item.Field<string>("Name"),
                                       IsConcentrator = Convert.ToBoolean(item.Field<object>("IsConcentrator")),
                                       CompanyID = item.Field<int?>("CompanyID"),
                                       VendorDeviceID = item.Field<int?>("VendorDeviceID"),
                                       Longitude = item.Field<decimal?>("Longitude"),
                                       Latitude = item.Field<decimal?>("Latitude"),
                                       InterconnectionID = item.Field<int?>("InterconnectionID"),
                                       Planned = Convert.ToBoolean(item.Field<object>("Planned")),
                                       Desired = Convert.ToBoolean(item.Field<object>("Desired")),
                                       InProgress = Convert.ToBoolean(item.Field<object>("InProgress")),
                                       CompanyName = item.Field<string>("CompanyName"),
                                       VendorDeviceName = item.Field<string>("VendorDeviceName"),
                                       InterconnectionName = item.Field<string>("InterconnectionName")
                                   }).ToList();				
                return otherDeviceList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string SaveOtherDevice(OtherDevice otherDevice, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (isNew)
                    command.CommandText = "Insert Into OtherDevice (Acronym, Name, IsConcentrator, CompanyID, VendorDeviceID, Longitude, Latitude, InterconnectionID, Planned, Desired, InProgress) Values " +
                        "(@acronym, @name, @isConcentrator, @companyID, @vendorDeviceID, @longitude, @latitude, @interconnectionID, @planned, @desired, @inProgress)";
                else
                    command.CommandText = "Update OtherDevice Set Acronym = @acronym, Name = @name, IsConcentrator = @isConcentrator, CompanyID = @companyID, VendorDeviceID = @vendorDeviceID, Longitude = @longitude, " +
                        "Latitude = @latitude, InterconnectionID = @interconnectionID, Planned = @planned, Desired = @desired, InProgress = @inProgress Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@acronym", otherDevice.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", otherDevice.Name));
                command.Parameters.Add(AddWithValue(command, "@isConcentrator", otherDevice.IsConcentrator));
                command.Parameters.Add(AddWithValue(command, "@companyID", otherDevice.CompanyID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@vendorDeviceID", otherDevice.VendorDeviceID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@longitude", otherDevice.Longitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@latitude", otherDevice.Latitude ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@interconnectionID", otherDevice.InterconnectionID ?? (object)DBNull.Value));
                command.Parameters.Add(AddWithValue(command, "@planned", otherDevice.Planned));
                command.Parameters.Add(AddWithValue(command, "@desired", otherDevice.Desired));
                command.Parameters.Add(AddWithValue(command, "@inProgress", otherDevice.InProgress));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", otherDevice.ID));

                command.ExecuteScalar();
                return "Other Device Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static OtherDevice GetOtherDeviceByDeviceID(int deviceID)
        {
            try
            {
                List<OtherDevice> otherDeviceList = new List<OtherDevice>();
                otherDeviceList = (from item in GetOtherDeviceList()
                                   where item.ID == deviceID
                                   select item).ToList();
                if (otherDeviceList.Count > 0)
                    return otherDeviceList[0];
                else
                    return null;
            }
            catch (Exception ex)
            {
                LogException("GetOtherDeviceByDeviceID", ex);
                return null;
            }
        }

        #endregion

        #region " Manage Interconnections Code"

        public static Dictionary<int, string> GetInterconnections(bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> interconnectionList = new Dictionary<int, string>();
                if (isOptional)
                    interconnectionList.Add(0, "Select Interconnection");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Name From Interconnection Order By LoadOrder";

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!interconnectionList.ContainsKey(Convert.ToInt32(row["ID"])))
                        interconnectionList.Add(Convert.ToInt32(row["ID"]), row["Name"].ToString());
                }				
                return interconnectionList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Protocols Code"

        public static Dictionary<int, string> GetProtocols(bool isOptional)
        {
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> protocolList = new Dictionary<int, string>();
                if (isOptional)
                    protocolList.Add(0, "Select Protocol");


                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Name From Protocol Order By LoadOrder";
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!protocolList.ContainsKey(Convert.ToInt32(row["ID"])))
                        protocolList.Add(Convert.ToInt32(row["ID"]), row["Name"].ToString());
                }								
                return protocolList;
            }
            
            finally
            {
                connection.Dispose();
            }
        }

        public static int GetProtocolIDByAcronym(string acronym)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID From Protocol Where Acronym = @acronym";
                command.Parameters.Add(AddWithValue(command, "@acronym", acronym));
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());				
                if (resultTable.Rows.Count > 0)
                    return Convert.ToInt32(resultTable.Rows[0]["ID"]);
                else
                    return 0;
            }
            catch (Exception ex)
            {
                LogException("GetProtocolIDByAcronym", ex);
                return 0;
            }
            finally
            {
                connection.Dispose();
            }
        }

        private static string GetProtocolAcronymByID(int id)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select Acronym From Protocol Where ID = @id";
                command.Parameters.Add(AddWithValue(command, "@id", id));
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                if (resultTable.Rows.Count > 0)
                    return resultTable.Rows[0]["Acronym"].ToString();
                else
                    return string.Empty;
            }
            catch (Exception ex)
            {
                LogException("GetProtocolAcronymByID", ex);
                return string.Empty;
            }
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Signal Types Code"

        public static Dictionary<int, string> GetSignalTypes(bool isOptional)
        {	
            DataConnection connection = new DataConnection();
            try
            {
                Dictionary<int, string> signalTypeList = new Dictionary<int, string>();
                if (isOptional)
                    signalTypeList.Add(0, "Select Signal Type");

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID, Name From SignalType Order By Name";
                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                foreach (DataRow row in resultTable.Rows)
                {
                    if (!signalTypeList.ContainsKey(Convert.ToInt32(row["ID"])))
                        signalTypeList.Add(Convert.ToInt32(row["ID"]), row["Name"].ToString());
                }
                                
                return signalTypeList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        static DataTable GetPmuSignalTypes()	//Do not use this method in WCF call or silverlight. It is for internal use only.
        {
            DataConnection connection = new DataConnection();
            DataTable resultTable = new DataTable();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From SignalType Where Source = 'PMU' AND Suffix IN ('FQ', 'DF', 'SF')";

                resultTable.Load(command.ExecuteReader());
            }
            catch (Exception ex)
            {
                LogException("GetPmuSignalTypes", ex);
            }
            finally
            {
                connection.Dispose();
            }
            return resultTable;
        }

        static DataTable GetPhasorSignalTypes(string phasorType)
        {
            DataConnection connection = new DataConnection();
            DataTable resultTable = new DataTable();
            try
            {

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (phasorType == "V")
                    command.CommandText = "Select * From SignalType Where Source = 'Phasor' AND Acronym LIKE 'V%'";
                else
                    command.CommandText = "Select * From SignalType Where Source = 'Phasor' AND Acronym LIKE 'I%'";
                
                resultTable.Load(command.ExecuteReader());								
            }
            catch (Exception ex)
            {
                LogException("GetPhasorSignalTypes", ex);
            }
            finally
            {
                connection.Dispose();
            }
            return resultTable;
        }

        static int GetSignalTypeID(string suffix)
        {
            int signalTypeID = 0;
            DataConnection connection = new DataConnection();			
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select ID From SignalType Where Suffix = @suffix";
                command.Parameters.Add(AddWithValue(command, "@suffix", suffix));
                signalTypeID = (int)command.ExecuteScalar();
            }
            catch (Exception ex)
            {
                LogException("GetSignalTypeID", ex);
            }
            finally
            {
                connection.Dispose();
            }
            return signalTypeID;
        }

        #endregion

        #region " Manage Calculated Measurements"

        public static List<CalculatedMeasurement> GetCalculatedMeasurementList(string nodeID)
        {			
            DataConnection connection = new DataConnection();
            try
            {
                List<CalculatedMeasurement> calculatedMeasurementList = new List<CalculatedMeasurement>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select * From CalculatedMeasurementDetail Order By LoadOrder";
                else
                {
                    command.CommandText = "Select * From CalculatedMeasurementDetail Where NodeID = @nodeID Order By LoadOrder";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                }

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                calculatedMeasurementList = (from item in resultTable.AsEnumerable()
                                             select new CalculatedMeasurement()
                                             {
                                                 NodeId = item.Field<object>("NodeID").ToString(),
                                                 ID = item.Field<int>("ID"),
                                                 Acronym = item.Field<string>("Acronym"),
                                                 Name = item.Field<string>("Name"),
                                                 AssemblyName = item.Field<string>("AssemblyName"),
                                                 TypeName = item.Field<string>("TypeName"),
                                                 ConnectionString = item.Field<string>("ConnectionString"),
                                                 ConfigSection = item.Field<string>("ConfigSection"),
                                                 InputMeasurements = item.Field<string>("InputMeasurements"),
                                                 OutputMeasurements = item.Field<string>("OutputMeasurements"),
                                                 MinimumMeasurementsToUse = item.Field<int>("MinimumMeasurementsToUse"),
                                                 FramesPerSecond = Convert.ToInt32(item.Field<object>("FramesPerSecond") ?? 30),
                                                 LagTime = item.Field<double>("LagTime"),
                                                 LeadTime = item.Field<double>("LeadTime"),
                                                 UseLocalClockAsRealTime = Convert.ToBoolean(item.Field<object>("UseLocalClockAsRealTime")),
                                                 AllowSortsByArrival = Convert.ToBoolean(item.Field<object>("AllowSortsByArrival")),
                                                 LoadOrder = item.Field<int>("LoadOrder"),
                                                 Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                                 IgnoreBadTimeStamps = Convert.ToBoolean(item.Field<object>("IgnoreBadTimeStamps")),
                                                 TimeResolution = Convert.ToInt32(item.Field<object>("TimeResolution")),
                                                 AllowPreemptivePublishing = Convert.ToBoolean(item.Field<object>("AllowPreemptivePublishing")),
                                                 DownsamplingMethod = item.Field<string>("DownSamplingMethod"),
                                                 NodeName = item.Field<string>("NodeName")
                                             }).ToList();
                                
                return calculatedMeasurementList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static string SaveCalculatedMeasurement(CalculatedMeasurement calculatedMeasurement, bool isNew)
        {
            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into CalculatedMeasurement (NodeID, Acronym, Name, AssemblyName, TypeName, ConnectionString, ConfigSection, InputMeasurements, OutputMeasurements, MinimumMeasurementsToUse, FramesPerSecond, LagTime, LeadTime, " +
                        "UseLocalClockAsRealTime, AllowSortsByArrival, LoadOrder, Enabled, IgnoreBadTimeStamps, TimeResolution, AllowPreemptivePublishing, DownsamplingMethod) Values (@nodeID, @acronym, @name, @assemblyName, @typeName, @connectionString, @configSection, @inputMeasurements, @outputMeasurements, @minimumMeasurementsToUse, " +
                        "@framesPerSecond, @lagTime, @leadTime, @useLocalClockAsRealTime, @allowSortsByArrival, @loadOrder, @enabled, @ignoreBadTimeStamps, @timeResolution, @allowPreemptivePublishing, @downsamplingMethod)";
                else
                    command.CommandText = "Update CalculatedMeasurement Set NodeID = @nodeID, Acronym = @acronym, Name = @name, AssemblyName = @assemblyName, TypeName = @typeName, ConnectionString = @connectionString, ConfigSection = @configSection, " +
                        "InputMeasurements = @inputMeasurements, OutputMeasurements = @outputMeasurements, MinimumMeasurementsToUse = @minimumMeasurementsToUse, FramesPerSecond = @framesPerSecond, LagTime = @lagTime, LeadTime = @leadTime, " +
                        "UseLocalClockAsRealTime = @useLocalClockAsRealTime, AllowSortsByArrival = @allowSortsByArrival, LoadOrder = @loadOrder, Enabled = @enabled, " +
                        "IgnoreBadTimeStamps = @ignoreBadTimeStamps, TimeResolution = @timeResolution, AllowPreemptivePublishing = @allowPreemptivePublishing, DownsamplingMethod = @downsamplingMethod Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", calculatedMeasurement.NodeId));
                command.Parameters.Add(AddWithValue(command, "@acronym", calculatedMeasurement.Acronym));
                command.Parameters.Add(AddWithValue(command, "@name", calculatedMeasurement.Name));
                command.Parameters.Add(AddWithValue(command, "@assemblyName", calculatedMeasurement.AssemblyName));
                command.Parameters.Add(AddWithValue(command, "@typeName", calculatedMeasurement.TypeName));
                command.Parameters.Add(AddWithValue(command, "@connectionString", calculatedMeasurement.ConnectionString));
                command.Parameters.Add(AddWithValue(command, "@configSection", calculatedMeasurement.ConfigSection));
                command.Parameters.Add(AddWithValue(command, "@inputMeasurements", calculatedMeasurement.InputMeasurements));
                command.Parameters.Add(AddWithValue(command, "@outputMeasurements", calculatedMeasurement.OutputMeasurements));
                command.Parameters.Add(AddWithValue(command, "@minimumMeasurementsToUse", calculatedMeasurement.MinimumMeasurementsToUse));
                command.Parameters.Add(AddWithValue(command, "@framesPerSecond", calculatedMeasurement.FramesPerSecond));
                command.Parameters.Add(AddWithValue(command, "@lagTime", calculatedMeasurement.LagTime));
                command.Parameters.Add(AddWithValue(command, "@leadTime", calculatedMeasurement.LeadTime));
                command.Parameters.Add(AddWithValue(command, "@useLocalClockAsRealTime", calculatedMeasurement.UseLocalClockAsRealTime));
                command.Parameters.Add(AddWithValue(command, "@allowSortsByArrival", calculatedMeasurement.AllowSortsByArrival));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", calculatedMeasurement.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", calculatedMeasurement.Enabled));
                command.Parameters.Add(AddWithValue(command, "@ignoreBadTimeStamps", calculatedMeasurement.IgnoreBadTimeStamps));
                command.Parameters.Add(AddWithValue(command, "@timeResolution", calculatedMeasurement.TimeResolution));
                command.Parameters.Add(AddWithValue(command, "@allowPreemptivePublishing", calculatedMeasurement.AllowPreemptivePublishing));
                command.Parameters.Add(AddWithValue(command, "@downsamplingMethod", calculatedMeasurement.DownsamplingMethod));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", calculatedMeasurement.ID));

                command.ExecuteNonQuery();

                return "Calculated Measurement Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Custom Adapters Code"

        public static List<Adapter> GetAdapterList(bool enabledOnly, AdapterType adapterType, string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<Adapter> adapterList = new List<Adapter>();
                string viewName;
                if (adapterType == AdapterType.Action)
                    viewName = "CustomActionAdapterDetail";
                else if (adapterType == AdapterType.Input)
                    viewName = "CustomInputAdapterDetail";
                else
                    viewName = "CustomOutputAdapterDetail";

                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                if (string.IsNullOrEmpty(nodeID) || MasterNode(nodeID))
                    command.CommandText = "Select * From " + viewName + " Order By LoadOrder";
                else
                {
                    command.CommandText = "Select * From " + viewName + " Where NodeID = @nodeID Order By LoadOrder";
                    //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                    if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                        command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                    else
                        command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                }

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());
                adapterList = (from item in resultTable.AsEnumerable()
                               select new Adapter()
                               {
                                   NodeID = item.Field<object>("NodeID").ToString(),
                                   ID = item.Field<int>("ID"),
                                   AdapterName = item.Field<string>("AdapterName"),
                                   AssemblyName = item.Field<string>("AssemblyName"),
                                   TypeName = item.Field<string>("TypeName"),
                                   ConnectionString = item.Field<string>("ConnectionString"),
                                   LoadOrder = item.Field<int>("LoadOrder"),
                                   Enabled = Convert.ToBoolean(item.Field<object>("Enabled")),
                                   NodeName = item.Field<string>("NodeName"),
                                   adapterType = adapterType
                               }).ToList();
                
                return adapterList;
            }			
            finally
            {
                connection.Dispose();
            }			
        }

        public static string SaveAdapter(Adapter adapter, bool isNew)
        {
            string tableName;
            AdapterType adapterType = adapter.adapterType;

            if (adapterType == AdapterType.Input)
                tableName = "CustomInputAdapter";
            else if (adapterType == AdapterType.Action)
                tableName = "CustomActionAdapter";
            else
                tableName = "CustomOutputAdapter";

            DataConnection connection = new DataConnection();
            try
            {
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (isNew)
                    command.CommandText = "Insert Into " + tableName + " (NodeID, AdapterName, AssemblyName, TypeName, ConnectionString, LoadOrder, Enabled) Values " +
                        "(@nodeID, @adapterName, @assemblyName, @typeName, @connectionString, @loadOrder, @enabled)";
                else
                    command.CommandText = "Update " + tableName + " Set NodeID = @nodeID, AdapterName = @adapterName, AssemblyName = @assemblyName, TypeName = @typeName, " +
                        "ConnectionString = @connectionString, LoadOrder = @loadOrder, Enabled = @enabled Where ID = @id";

                command.Parameters.Add(AddWithValue(command, "@nodeID", adapter.NodeID));
                command.Parameters.Add(AddWithValue(command, "@adapterName", adapter.AdapterName));
                command.Parameters.Add(AddWithValue(command, "@assemblyName", adapter.AssemblyName));
                command.Parameters.Add(AddWithValue(command, "@typeName", adapter.TypeName));
                command.Parameters.Add(AddWithValue(command, "@connectionString", adapter.ConnectionString));
                command.Parameters.Add(AddWithValue(command, "@loadOrder", adapter.LoadOrder));
                command.Parameters.Add(AddWithValue(command, "@enabled", adapter.Enabled));

                if (!isNew)
                    command.Parameters.Add(AddWithValue(command, "@id", adapter.ID));

                command.ExecuteNonQuery();

                return "Adapter Information Saved Successfully";
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static List<IaonTree> GetIaonTreeData(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<IaonTree> iaonTreeList = new List<IaonTree>();
                DataTable rootNodesTable = new DataTable();
                rootNodesTable.Columns.Add(new DataColumn("AdapterType", Type.GetType("System.String")));

                DataRow row;
                row = rootNodesTable.NewRow();
                row["AdapterType"] = "Input Adapters";
                rootNodesTable.Rows.Add(row);

                row = rootNodesTable.NewRow();
                row["AdapterType"] = "Action Adapters";
                rootNodesTable.Rows.Add(row);

                row = rootNodesTable.NewRow();
                row["AdapterType"] = "Output Adapters";
                rootNodesTable.Rows.Add(row);

                DataSet resultSet = new DataSet();
                resultSet.Tables.Add(rootNodesTable);
                                
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "Select * From IaonTreeView Where NodeID = @nodeID";
                //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                else
                    command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));

                DataTable resultTable = new DataTable();
                resultSet.EnforceConstraints = false;	//this is needed to make it work against mySQL.
                resultSet.Tables.Add(resultTable);
                resultTable.Load(command.ExecuteReader());
                resultSet.Tables[0].TableName = "RootNodesTable";
                resultSet.Tables[1].TableName = "AdapterData";

                iaonTreeList = (from item in resultSet.Tables["RootNodesTable"].AsEnumerable()
                                select new IaonTree()
                                {
                                    AdapterType = item.Field<string>("AdapterType"),
                                    AdapterList = (from obj in resultSet.Tables["AdapterData"].AsEnumerable()
                                                   where obj.Field<string>("AdapterType") == item.Field<string>("AdapterType")
                                                   select new Adapter()
                                                   {
                                                       NodeID = obj.Field<object>("NodeID").ToString(),
                                                       ID = Convert.ToInt32(obj.Field<object>("ID")),
                                                       AdapterName = obj.Field<string>("AdapterName"),
                                                       AssemblyName = obj.Field<string>("AssemblyName"),
                                                       TypeName = obj.Field<string>("TypeName"),
                                                       ConnectionString = obj.Field<string>("ConnectionString")
                                                   }).ToList()
                                }).ToList();
                
                return iaonTreeList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Manage Map Data"

        public static List<MapData> GetMapData(MapType mapType, string nodeID)
        {						
            DataConnection connection = new DataConnection();
            try
            {
                List<MapData> mapDataList = new List<MapData>();
                IDbCommand command = connection.Connection.CreateCommand();
                command.CommandType = CommandType.Text;

                if (mapType == MapType.Active)				
                    command.CommandText = "Select * From MapData Where NodeID = @nodeID and DeviceType = 'Device'";									
                else
                    command.CommandText = "Select * From MapData Where NodeID = @nodeID UNION ALL Select * From MapData Where NodeID IS NULL";

                //command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));
                if (command.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    command.Parameters.Add(AddWithValue(command, "@nodeID", "{" + nodeID + "}"));
                else
                    command.Parameters.Add(AddWithValue(command, "@nodeID", nodeID));

                DataTable resultTable = new DataTable();
                resultTable.Load(command.ExecuteReader());

                if (mapType == MapType.Active)
                    mapDataList = (from item in resultTable.AsEnumerable()
                                   select new MapData()
                                   {
                                       NodeID = item.Field<object>("NodeID").ToString(),
                                       ID = item.Field<int>("ID"),
                                       Acronym = item.Field<string>("Acronym"),
                                       Name = item.Field<string>("Name"),
                                       CompanyMapAcronym = item.Field<string>("CompanyMapAcronym"),
                                       CompanyName = item.Field<string>("CompanyName"),
                                       VendorDeviceName = item.Field<string>("VendorDeviceName"),
                                       Longitude = item.Field<decimal?>("Longitude"),
                                       Latitude = item.Field<decimal?>("Latitude"),
                                       Reporting = Convert.ToBoolean(item.Field<object>("Reporting"))
                                   }).ToList();
                else
                    mapDataList = (from item in resultTable.AsEnumerable()
                                   select new MapData()
                                   {
                                       DeviceType = item.Field<string>("DeviceType"),
                                       ID = item.Field<int>("ID"),
                                       Acronym = item.Field<string>("Acronym"),
                                       Name = item.Field<string>("Name"),
                                       CompanyMapAcronym = item.Field<string>("CompanyMapAcronym"),
                                       CompanyName = item.Field<string>("CompanyName"),
                                       VendorDeviceName = item.Field<string>("VendorDeviceName"),
                                       Longitude = item.Field<decimal?>("Longitude"),
                                       Latitude = item.Field<decimal?>("Latitude"),
                                       Reporting = Convert.ToBoolean(item.Field<object>("Reporting")),
                                       InProgress = Convert.ToBoolean(item.Field<object>("InProgress")),
                                       Planned = Convert.ToBoolean(item.Field<object>("Planned")),
                                       Desired = Convert.ToBoolean(item.Field<object>("Desired"))
                                   }).ToList();
                                
                return mapDataList;
            }			
            finally
            {
                connection.Dispose();
            }
        }

        #endregion			
    
        #region " Current Device Measurements Code"

        public static ObservableCollection<DeviceMeasurementData> GetDeviceMeasurementData(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                List<DeviceMeasurementData> deviceMeasurementDataList = new List<DeviceMeasurementData>();
                DataSet resultSet = new DataSet();
                resultSet.EnforceConstraints = false;

                DataTable resultTable;

                IDbCommand commandPdc = connection.Connection.CreateCommand();
                commandPdc.CommandType = CommandType.Text;				
                //Get PDCs list
                commandPdc.CommandText = "Select ID, Acronym, Name, CompanyName From DeviceDetail Where NodeID = @nodeID AND IsConcentrator = @isConcentrator";
                if (commandPdc.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandPdc.Parameters.Add(AddWithValue(commandPdc, "@nodeID", "{" + nodeID + "}"));
                else
                    commandPdc.Parameters.Add(AddWithValue(commandPdc, "@nodeID", nodeID));

                commandPdc.Parameters.Add(AddWithValue(commandPdc, "@isConcentrator", true));

                resultTable = new DataTable();
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandPdc.ExecuteReader());
                DataRow row = resultTable.NewRow();
                row["ID"] = 0;
                row["Acronym"] = string.Empty;
                row["Name"] = "Devices Connected Directly";
                row["CompanyName"] = string.Empty;
                resultTable.Rows.Add(row);

                //Get Non PDC Devices
                IDbCommand commandDevices = connection.Connection.CreateCommand();
                commandDevices.CommandType = CommandType.Text;
                commandDevices.CommandText = "Select ID, Acronym, Name,CompanyName, ProtocolName, VendorDeviceName, ParentAcronym From DeviceDetail Where NodeID = @nodeID AND IsConcentrator = @isConcentrator";
                if (commandDevices.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandDevices.Parameters.Add(AddWithValue(commandDevices, "@nodeID", "{" + nodeID + "}"));
                else
                    commandDevices.Parameters.Add(AddWithValue(commandDevices, "@nodeID", nodeID));

                commandDevices.Parameters.Add(AddWithValue(commandDevices, "@isConcentrator", false));

                resultTable = new DataTable();
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandDevices.ExecuteReader());
                row = resultTable.NewRow();
                row["ID"] = DBNull.Value;
                row["Acronym"] = "CALCULATED";
                row["Name"] = "CALCULATED MEASUREMENTS";
                row["CompanyName"] = string.Empty;
                row["ProtocolName"] = string.Empty;
                row["VendorDeviceName"] = string.Empty;
                row["ParentAcronym"] = string.Empty;
                resultTable.Rows.Add(row);

                //Get Measurements
                IDbCommand commandMeasurements = connection.Connection.CreateCommand();
                commandMeasurements.CommandType = CommandType.Text;
                commandMeasurements.CommandText = "Select DeviceID, SignalID, PointID, PointTag, SignalReference, SignalAcronym, Description, SignalName, EngineeringUnits From MeasurementDetail Where NodeID = @nodeID AND SignalAcronym <> 'STAT'";
                if (commandMeasurements.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", "{" + nodeID + "}"));
                else
                    commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", nodeID));

                resultTable = new DataTable();
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandMeasurements.ExecuteReader());
                                
                resultSet.Tables[0].TableName = "PdcTable";
                resultSet.Tables[1].TableName = "DeviceTable";
                resultSet.Tables[2].TableName = "MeasurementTable";

                deviceMeasurementDataList = (from pdc in resultSet.Tables["PdcTable"].AsEnumerable()
                                             select new DeviceMeasurementData()
                                             {
                                                 ID = pdc.Field<int>("ID"),
                                                 Acronym = string.IsNullOrEmpty(pdc.Field<string>("Acronym")) ? "DIRECT CONNECTED" : pdc.Field<string>("Acronym"),
                                                 Name = pdc.Field<string>("Name"),
                                                 CompanyName = pdc.Field<string>("CompanyName"),
                                                 StatusColor = string.IsNullOrEmpty(pdc.Field<string>("Acronym")) ? "Transparent" : "Gray",
                                                 IsExpanded = false,
                                                 DeviceList = new ObservableCollection<DeviceInfo>((from device in resultSet.Tables["DeviceTable"].AsEnumerable()
                                                               where device.Field<string>("ParentAcronym") == pdc.Field<string>("Acronym")
                                                               select new DeviceInfo()
                                                               {
                                                                   ID = device.Field<int?>("ID"),
                                                                   Acronym = device.Field<string>("Acronym"),
                                                                   Name = device.Field<string>("Name"),
                                                                   CompanyName = device.Field<string>("CompanyName"),
                                                                   ProtocolName = device.Field<string>("ProtocolName"),
                                                                   VendorDeviceName = device.Field<string>("VendorDeviceName"),
                                                                   ParentAcronym = string.IsNullOrEmpty(device.Field<string>("ParentAcronym")) ? "DIRECT CONNECTED" : device.Field<string>("ParentAcronym"),
                                                                   IsExpanded = false,
                                                                   StatusColor = device.Field<int?>("ID") == null ? "Transparent" : "Gray",
                                                                   MeasurementList = new ObservableCollection<MeasurementInfo>((from measurement in resultSet.Tables["MeasurementTable"].AsEnumerable()
                                                                                      where measurement.Field<int?>("DeviceID") == device.Field<int?>("ID")
                                                                                      select new MeasurementInfo()
                                                                                      {
                                                                                          DeviceID = measurement.Field<int?>("DeviceID"),
                                                                                          SignalID = measurement.Field<object>("SignalID").ToString(),
                                                                                          PointID = measurement.Field<int>("PointID"),
                                                                                          PointTag = measurement.Field<string>("PointTag"),
                                                                                          SignalReference = measurement.Field<string>("SignalReference"),
                                                                                          SignalAcronym = measurement.Field<string>("SignalAcronym"),
                                                                                          Description = measurement.Field<string>("description"),
                                                                                          SignalName = measurement.Field<string>("SignalName"),
                                                                                          EngineeringUnits = measurement.Field<string>("EngineeringUnits"),
                                                                                          IsExpanded = false,
                                                                                          CurrentTimeTag = "N/A",
                                                                                          CurrentValue = "NaN",
                                                                                          CurrentQuality = "N/A"
                                                                                      }).ToList())
                                                               }).ToList())
                                             }).ToList();

                return new ObservableCollection<DeviceMeasurementData>(deviceMeasurementDataList);
            }
            
            finally
            {
                connection.Dispose();
            }
        }

        #endregion

        #region " Statistics Hierarchy Code"

        public static ObservableCollection<StatisticMeasurementData> GetStatisticMeasurementData(string nodeID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                ObservableCollection<StatisticMeasurementData> staticMeasurementDataList = new ObservableCollection<StatisticMeasurementData>();
                ObservableCollection<StreamInfo> inputStreamInfoList = new ObservableCollection<StreamInfo>();
                ObservableCollection<StreamInfo> outputStreamInfoList = new ObservableCollection<StreamInfo>();
               
                DataSet resultSet = new DataSet();
                resultSet.EnforceConstraints = false;
                DataTable resultTable;

                //****************************************************************************
                //First get all the PDC devices and directly connected devices. These are our input stream devices.                
                IDbCommand commandPdc = connection.Connection.CreateCommand();
                commandPdc.CommandType = CommandType.Text;                
                commandPdc.CommandText = "Select ID, Acronym, Name From DeviceDetail Where NodeID = @nodeID AND (IsConcentrator = @isConcentrator OR ParentAcronym = @parentAcronym) Order By Acronym";
                if (commandPdc.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandPdc.Parameters.Add(AddWithValue(commandPdc, "@nodeID", "{" + nodeID + "}"));
                else
                    commandPdc.Parameters.Add(AddWithValue(commandPdc, "@nodeID", nodeID));
                commandPdc.Parameters.Add(AddWithValue(commandPdc, "@isConcentrator", true));
                commandPdc.Parameters.Add(AddWithValue(commandPdc, "@parentAcronym", string.Empty));

                resultTable = new DataTable("InputStreamDevices");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandPdc.ExecuteReader());
                //****************************************************************************

                //****************************************************************************                
                //Second, get all the Devices that are connected to PDC. These devices are part of input stream coming from other PDCs.
                IDbCommand commandDevices = connection.Connection.CreateCommand();
                commandDevices.CommandType = CommandType.Text;
                commandDevices.CommandText = "Select ID, Acronym, Name, ParentID From DeviceDetail Where NodeID = @nodeID AND IsConcentrator = @isConcentrator AND ParentID > 0 Order By Acronym";
                if (commandDevices.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandDevices.Parameters.Add(AddWithValue(commandDevices, "@nodeID", "{" + nodeID + "}"));
                else
                    commandDevices.Parameters.Add(AddWithValue(commandDevices, "@nodeID", nodeID));
                commandDevices.Parameters.Add(AddWithValue(commandDevices, "@isConcentrator", false));

                resultTable = new DataTable("PdcDevices");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandDevices.ExecuteReader());
                //****************************************************************************

                //****************************************************************************
                //Get Output Stream information.
                IDbCommand commandOutputStream = connection.Connection.CreateCommand();
                commandOutputStream.CommandType = CommandType.Text;
                commandOutputStream.CommandText = "Select ID, Acronym, Name From OutputStream Where NodeID = @nodeID Order By Acronym";
                if (commandOutputStream.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandOutputStream.Parameters.Add(AddWithValue(commandOutputStream, "@nodeID", "{" + nodeID + "}"));
                else
                    commandOutputStream.Parameters.Add(AddWithValue(commandOutputStream, "@nodeID", nodeID));

                resultTable = new DataTable("OutputStreams");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandOutputStream.ExecuteReader());
                //****************************************************************************

                //****************************************************************************
                //Third, we need to get statistics measurements from Measurement table, statistic definition from Statistic table. Create relationship between those two.
                //Then create parent child relationship to above two datatables.
                IDbCommand commandMeasurements = connection.Connection.CreateCommand();
                commandMeasurements.CommandType = CommandType.Text;
                commandMeasurements.CommandText = "Select DeviceID, PointID, PointTag, SignalReference, MeasurementSource From StatisticMeasurement Where NodeID = @nodeID Order By MeasurementSource, SignalReference";
                if (commandMeasurements.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                    commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", "{" + nodeID + "}"));
                else
                    commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", nodeID));
                
                resultTable = new DataTable("StatisticMeasurements");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandMeasurements.ExecuteReader());
                //****************************************************************************

                //****************************************************************************
                //Get Statistic definitions.
                IDbCommand commandStatistics = connection.Connection.CreateCommand();
                commandStatistics.CommandType = CommandType.Text;
                commandStatistics.CommandText = "Select Source, SignalIndex, Name, Description, DataType, DisplayFormat, IsConnectedState, LoadOrder From Statistic Order By Source, SignalIndex";
                resultTable = new DataTable("StatisticDefinitions");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandStatistics.ExecuteReader());
                //****************************************************************************

                List<DetailStatisticInfo> statisticInfoList = new List<DetailStatisticInfo>();
                statisticInfoList = (from measurement in resultSet.Tables["StatisticMeasurements"].AsEnumerable()
                                     select new DetailStatisticInfo()
                                     {
                                         DeviceID = measurement.Field<object>("DeviceID") == null ? -1 : Convert.ToInt32(measurement.Field<object>("DeviceID")),
                                         PointID = Convert.ToInt32(measurement.Field<object>("PointID")),
                                         PointTag = measurement.Field<string>("PointTag"),
                                         SignalReference = measurement.Field<string>("SignalReference"),
                                         Statistics = (from statistic in resultSet.Tables["StatisticDefinitions"].AsEnumerable()
                                                       where statistic.Field<string>("Source") == measurement.Field<string>("MeasurementSource") &&
                                                          statistic.Field<int>("SignalIndex") == Convert.ToInt32((measurement.Field<string>("SignalReference")).Substring((measurement.Field<string>("SignalReference")).LastIndexOf("-ST") + 3))
                                                          select new BasicStatisticInfo()
                                                          {
                                                              Source = statistic.Field<string>("Source"),
                                                              Name = statistic.Field<string>("Name"),
                                                              Description = statistic.Field<string>("Description"),
                                                              Quality = "N/A",
                                                              TimeTag = "N/A",
                                                              Value = "N/A",
                                                              DataType = statistic.Field<object>("DataType") == null ? string.Empty : statistic.Field<string>("DataType"),
                                                              DisplayFormat = statistic.Field<object>("DisplayFormat") == null ? string.Empty : statistic.Field<string>("DisplayFormat"),
                                                              IsConnectedState = Convert.ToBoolean(statistic.Field<object>("IsConnectedState")),
                                                              LoadOrder = Convert.ToInt32(statistic.Field<object>("LoadOrder"))
                                                          }
                                                       ).First()
                                     }).ToList();
                                
                //****************************************************************************

                inputStreamInfoList = new ObservableCollection<StreamInfo>((from inputDevice in resultSet.Tables["InputStreamDevices"].AsEnumerable()
                                            select new StreamInfo()
                                            {
                                                ID = Convert.ToInt32(inputDevice.Field<object>("ID")),
                                                Acronym = inputDevice.Field<string>("Acronym"),
                                                Name = inputDevice.Field<string>("Name"),
                                                StatusColor = "Gray",
                                                StatisticList = new ObservableCollection<DetailStatisticInfo>( (from statistic in statisticInfoList
                                                                 where statistic.DeviceID == Convert.ToInt32(inputDevice.Field<object>("ID"))
                                                                 select statistic).ToList().OrderBy(o => o.Statistics.Source).ThenBy(o => o.Statistics.LoadOrder).ToList()),
                                                DeviceStatisticList = new ObservableCollection<DeviceStatistic>( (from pdcDevice in resultSet.Tables["PdcDevices"].AsEnumerable()
                                                              where Convert.ToInt32(pdcDevice.Field<object>("ParentID")) == Convert.ToInt32(inputDevice.Field<object>("ID"))
                                                              select new DeviceStatistic()
                                                              {
                                                                    ID = Convert.ToInt32(pdcDevice.Field<object>("ID")),
                                                                    Acronym = pdcDevice.Field<string>("Acronym"),
                                                                    Name = pdcDevice.Field<string>("Name"),
                                                                    StatisticList = new ObservableCollection<DetailStatisticInfo>((from statistic in statisticInfoList
                                                                                     where statistic.DeviceID == Convert.ToInt32(pdcDevice.Field<object>("ID"))
                                                                                    select statistic 
                                                                                    ).ToList().OrderBy( o => o.Statistics.LoadOrder).ToList())
                                                              }).ToList())
                                            }).ToList());

                foreach (StreamInfo inputStreamDevice in inputStreamInfoList)
                {
                    inputStreamDevice.DeviceStatisticList.Insert(0, new DeviceStatistic() { ID = 0, Acronym = "Run-Time Statistics", Name = "", StatisticList = new ObservableCollection<DetailStatisticInfo>(inputStreamDevice.StatisticList) });
                    inputStreamDevice.StatisticList = null;  //since this is moved to dummy device above "Run-Time Statistics", we don't need it anymore.
                }
                
                staticMeasurementDataList.Add(new StatisticMeasurementData() { SourceType = "Input Streams", SourceStreamInfoList = new ObservableCollection<StreamInfo>(inputStreamInfoList) });
                //****************************************************************************

                //****************************************************************************
                //Now create a list for output steams.
                outputStreamInfoList = new ObservableCollection<StreamInfo>( (from outputDevice in resultSet.Tables["OutputStreams"].AsEnumerable()
                                        select new StreamInfo()
                                        {
                                            ID = Convert.ToInt32(outputDevice.Field<object>("ID")),
                                            Acronym = outputDevice.Field<string>("Acronym"),
                                            Name = outputDevice.Field<string>("Name"),
                                            StatusColor = "Gray",
                                            StatisticList = new ObservableCollection<DetailStatisticInfo>((from statistic in statisticInfoList
                                                             where statistic.SignalReference.StartsWith(outputDevice.Field<string>("Acronym"))
                                                             select statistic).ToList().OrderBy(o => o.Statistics.Source).ThenBy(o => o.Statistics.LoadOrder).ToList()),
                                            DeviceStatisticList = new ObservableCollection<DeviceStatistic>()
                                        }).ToList());

                foreach (StreamInfo outputStreamDevice in outputStreamInfoList)
                {
                    outputStreamDevice.DeviceStatisticList.Insert(0, new DeviceStatistic() { ID = 0, Acronym = "Run-Time Statistics", Name = "", StatisticList = new ObservableCollection<DetailStatisticInfo>(outputStreamDevice.StatisticList) });
                    outputStreamDevice.StatisticList = null;
                }
                staticMeasurementDataList.Add(new StatisticMeasurementData() { SourceType = "Output Streams", SourceStreamInfoList = outputStreamInfoList });
                
                return staticMeasurementDataList;                
            }			
            finally
            {
                connection.Dispose();
            }
        }

        public static Dictionary<int, BasicStatisticInfo> GetBasicStatisticInfoList(string nodeID)
        {
             Dictionary<int, BasicStatisticInfo> basicStatisticInfoList = new Dictionary<int, BasicStatisticInfo>();

             DataConnection connection = new DataConnection();
             try
             {
                 DataSet resultSet = new DataSet();
                 resultSet.EnforceConstraints = false;
                 DataTable resultTable;

                 IDbCommand commandMeasurements = connection.Connection.CreateCommand();
                 commandMeasurements.CommandType = CommandType.Text;
                 commandMeasurements.CommandText = "Select DeviceID, PointID, PointTag, SignalReference, MeasurementSource From StatisticMeasurement Where NodeID = @nodeID Order By MeasurementSource, SignalReference";
                 if (commandMeasurements.Connection.ConnectionString.Contains("Microsoft.Jet.OLEDB"))
                     commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", "{" + nodeID + "}"));
                 else
                     commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@nodeID", nodeID));

                 resultTable = new DataTable("StatisticMeasurements");
                 resultSet.Tables.Add(resultTable);
                 resultTable.Load(commandMeasurements.ExecuteReader());
                 //****************************************************************************

                 //****************************************************************************
                 //Get Statistic definitions.
                 IDbCommand commandStatistics = connection.Connection.CreateCommand();
                 commandStatistics.CommandType = CommandType.Text;
                 commandStatistics.CommandText = "Select Source, SignalIndex, Name, Description, DataType, DisplayFormat, IsConnectedState, LoadOrder From Statistic Order By Source, SignalIndex";
                 resultTable = new DataTable("StatisticDefinitions");
                 resultSet.Tables.Add(resultTable);
                 resultTable.Load(commandStatistics.ExecuteReader());
                 //****************************************************************************
                               
                 var tempCollection = (from measurement in resultSet.Tables["StatisticMeasurements"].AsEnumerable()
                                           select new KeyValuePair<int, BasicStatisticInfo>(Convert.ToInt32(measurement.Field<object>("PointID")),
                                                (from statistic in resultSet.Tables["StatisticDefinitions"].AsEnumerable()
                                                         where statistic.Field<string>("Source") == measurement.Field<string>("MeasurementSource") &&
                                                         statistic.Field<int>("SignalIndex") == Convert.ToInt32((measurement.Field<string>("SignalReference")).Substring((measurement.Field<string>("SignalReference")).LastIndexOf("-ST") + 3))
                                                         select new BasicStatisticInfo()
                                                         {
                                                             Source = statistic.Field<string>("Source"),
                                                             Name = statistic.Field<string>("Name"),
                                                             Description = statistic.Field<string>("Description"),
                                                             Quality = "N/A",
                                                             TimeTag = "N/A",
                                                             Value = "N/A",
                                                             DataType = statistic.Field<object>("DataType") == null ? string.Empty : statistic.Field<string>("DataType"),
                                                             DisplayFormat = statistic.Field<object>("DisplayFormat") == null ? string.Empty : statistic.Field<string>("DisplayFormat"),
                                                             IsConnectedState = Convert.ToBoolean(statistic.Field<object>("IsConnectedState")),
                                                             LoadOrder = Convert.ToInt32(statistic.Field<object>("LoadOrder"))
                                                         }
                                                        ).First()
                                           )).Distinct();

                 foreach (var item in tempCollection)
                 {
                     basicStatisticInfoList.Add(item.Key, item.Value);
                 }
             }
             catch (Exception ex)
             {
                 LogException("GetBasicStatisticInfoList", ex);                 
             }
             finally
             {
                 connection.Dispose();
             }

            return basicStatisticInfoList;
        }

        public static ObservableCollection<DetailStatisticInfo> GetDeviceStatisticMeasurements(int deviceID)
        {
            DataConnection connection = new DataConnection();
            try
            {
                ObservableCollection<DetailStatisticInfo> deviceStatisticList;
                DataSet resultSet = new DataSet();
                resultSet.EnforceConstraints = false;
                DataTable resultTable;

                IDbCommand commandMeasurements = connection.Connection.CreateCommand();
                commandMeasurements.CommandType = CommandType.Text;
                commandMeasurements.CommandText = "Select DeviceID, PointID, PointTag, SignalReference, MeasurementSource From StatisticMeasurement Where DeviceID = @deviceID Order By MeasurementSource, SignalReference";
                commandMeasurements.Parameters.Add(AddWithValue(commandMeasurements, "@deviceID", deviceID));
                
                resultTable = new DataTable("StatisticMeasurements");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandMeasurements.ExecuteReader());

                //****************************************************************************
                //Get Statistic definitions.
                IDbCommand commandStatistics = connection.Connection.CreateCommand();
                commandStatistics.CommandType = CommandType.Text;
                commandStatistics.CommandText = "Select Source, SignalIndex, Name, Description, DataType, DisplayFormat, IsConnectedState, LoadOrder From Statistic Order By Source, SignalIndex";
                resultTable = new DataTable("StatisticDefinitions");
                resultSet.Tables.Add(resultTable);
                resultTable.Load(commandStatistics.ExecuteReader());
                //****************************************************************************

                deviceStatisticList = new ObservableCollection<DetailStatisticInfo>(
                                     (from measurement in resultSet.Tables["StatisticMeasurements"].AsEnumerable()
                                      select new DetailStatisticInfo()
                                      {
                                          DeviceID = measurement.Field<object>("DeviceID") == null ? -1 : Convert.ToInt32(measurement.Field<object>("DeviceID")),
                                          PointID = Convert.ToInt32(measurement.Field<object>("PointID")),
                                          PointTag = measurement.Field<string>("PointTag"),
                                          SignalReference = measurement.Field<string>("SignalReference"),
                                          Statistics = (from statistic in resultSet.Tables["StatisticDefinitions"].AsEnumerable()
                                                        where statistic.Field<string>("Source") == measurement.Field<string>("MeasurementSource") &&
                                                           statistic.Field<int>("SignalIndex") == Convert.ToInt32((measurement.Field<string>("SignalReference")).Substring((measurement.Field<string>("SignalReference")).LastIndexOf("-ST") + 3))
                                                        select new BasicStatisticInfo()
                                                        {
                                                            Source = statistic.Field<string>("Source"),
                                                            Name = statistic.Field<string>("Name"),
                                                            Description = statistic.Field<string>("Description"),
                                                            Quality = "N/A",
                                                            TimeTag = "N/A",
                                                            Value = "N/A",
                                                            DataType = statistic.Field<object>("DataType") == null ? string.Empty : statistic.Field<string>("DataType"),
                                                            DisplayFormat = statistic.Field<object>("DisplayFormat") == null ? string.Empty : statistic.Field<string>("DisplayFormat"),
                                                            IsConnectedState = Convert.ToBoolean(statistic.Field<object>("IsConnectedState")),
                                                            LoadOrder = Convert.ToInt32(statistic.Field<object>("LoadOrder"))
                                                        }
                                                        ).First()
                                      }).ToList());

                return deviceStatisticList;
            }
            catch (Exception ex)
            {
                LogException("GetDeviceStatisticMeasurements", ex);
                return new ObservableCollection<DetailStatisticInfo>();
            }
            finally
            {
                connection.Dispose();
            }
        }

        //This function returns DeviceID with Statistic Measurement PointID which is used to check device status (green or red).
        public static Dictionary<int, int> GetDeviceIDsWithStatusPointIDs(string nodeID)
        {
            Dictionary<int, int> deviceIdsWithStatusPointIDs = new Dictionary<int, int>();
            DataConnection connection = new DataConnection();
            try
            {
                List<Device> deviceList = GetDeviceList(nodeID);
                foreach (Device device in deviceList)
                {
                    int deviceIDToLookFor;
                    if (device.ParentID == null)
                        deviceIDToLookFor = device.ID;
                    else
                        deviceIDToLookFor = (int)device.ParentID;
                    ObservableCollection<DetailStatisticInfo> detailStatisticsList = GetDeviceStatisticMeasurements(deviceIDToLookFor);
                    foreach (DetailStatisticInfo detailStatistic in detailStatisticsList)
                    {
                        if (detailStatistic.Statistics.IsConnectedState && !deviceIdsWithStatusPointIDs.ContainsKey(deviceIDToLookFor))
                            deviceIdsWithStatusPointIDs.Add(device.ID, detailStatistic.PointID);
                    }
                }
            }
            catch (Exception ex)
            {
                LogException("GetDeviceIDsWithStatusPointIDs", ex);
            }
            finally 
            {
                connection.Dispose();
            }

            return deviceIdsWithStatusPointIDs;
        }

        #endregion
    }
}
