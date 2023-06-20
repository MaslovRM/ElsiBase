using ADClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DirRX.ADIntegrationCore.Constants;
using DirRX.ADIntegrationCore;
using DirRX.ADIntegrationCore.Structures.Module;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.CoreEntities.Client;
using Sungero.CoreEntities.Login;
using Sungero.CoreEntities.Shared;
using Sungero.Domain.Shared;
using Sungero.Parties;
using Sungero.Workflow;

namespace DirRX.ADIntegrationCore.Client
{
  /// <summary>
  /// Класс клиентских функций модуля.
  /// </summary>
  public class ModuleFunctions
  {  
    #region Функции отправки задач
  	
  	/// <summary>
  	/// Получить из настройки список пользователей, которым нужно отправлять уведомление.
  	/// </summary>
  	/// <returns>Список пользователей, которым нужно отправлять уведомление.</returns>
  	private List<IRecipient> GetNotificationPerformers()
  	{
  		var notificationRoleName = SettingManager.Instance.NotificationRoleName;
  	  if (string.IsNullOrEmpty(notificationRoleName))
  		  notificationRoleName = "Администраторы";
  	  return Functions.Module.Remote.GetNotificationPerformers(notificationRoleName);
  	}
  	
  	/// <summary>
  	/// Отправить уведомление об ошибках.
  	/// </summary>
  	/// <param name="unsavedEmployees">Несохраненные сотрудники.</param>
  	protected virtual void SendErrorNotification(List<UnsavedInfo> unsavedEmployees)
  	{
      if (unsavedEmployees.Any())
      {
	      var notificationPerformers = GetNotificationPerformers();
      	string unsavedEntitiesText = "Не удалось сохранить следующих сотрудников:" + Environment.NewLine;
      	string unsavedEntitiesTextForLog = unsavedEntitiesText;
				foreach (var unsavedInfo in unsavedEmployees)
				{
					var currentLine = string.Format(" - ИД {0} (имя {1}) произошла ошибка {2}", unsavedInfo.Id, unsavedInfo.DisplayValue, unsavedInfo.ErrorMessage);
					unsavedEntitiesTextForLog += currentLine;
        	unsavedEntitiesText += currentLine + " (подробнее см. лог-файл).";
        	unsavedEntitiesTextForLog += Environment.NewLine;
        	unsavedEntitiesText += Environment.NewLine;
				}
				Logger.Error(unsavedEntitiesTextForLog);
				Functions.Module.Remote.SendNotice("Ошибки синхронизации сотрудников", unsavedEntitiesText, notificationPerformers, null);
      }
  	}
  	
  	/// <summary>
  	/// Отправить уведомление со списком сгенерированных пользователей и их паролей.
  	/// </summary>
  	/// <param name="credentials">Список с информацией о сгенерированных учетных данных.</param>
  	protected virtual void SendCredentialNotification(List<DirRX.ADIntegrationCore.Structures.Module.Credential> credentials)
  	{
  		if (credentials.Any())
  		{
  		  string notificationText;
  		  if (SettingManager.Instance.AuthenticationType == AuthenticationTypeConstants.Windows)
  		  {
  		    notificationText = "Сгенерированы следующие учетные записи с Windows аутентификацией:" + Environment.NewLine;
          foreach (var credential in credentials)
          	notificationText += string.Format(" - логин {0}{1}", credential.LoginName, Environment.NewLine);
  		  }
  		  else
  		  {
  		    notificationText = "Сгенерированы следующие учетные записи с парольной аутентификацией:" + Environment.NewLine;
          foreach (var credential in credentials)
          	notificationText += string.Format(" - логин {0}, пароль {1}{2}", credential.LoginName, credential.Password, Environment.NewLine);
  		  }

		  	var notificationPerformers = GetNotificationPerformers();
        Functions.Module.Remote.SendNotice("Сгенерированы новые учетные записи", notificationText, notificationPerformers, null);
  		}
  	}
  	
  	/// <summary>
  	/// Отправить задачу о необходимости заполнить подразделение для сотрудников.
  	/// </summary>
  	/// <param name="employeesWithoutDepartment">Список сотрудников без подразделения.</param>
  	protected virtual void SendDetermineDepartmentTask(List<EmployeeInfo> employeesWithoutDepartment)
  	{
  		if (employeesWithoutDepartment.Any())
  		{
  			var notificationPerformers = GetNotificationPerformers();
  			var taskText = "Необходимо заполнить подразделение у следующих сотрудников:" + Environment.NewLine;
  			var attachments = new List<IEntity>();
  			foreach (var employeeInfo in employeesWithoutDepartment)
  			{
  				var employee = Functions.Module.Remote.GetEmployeeById(employeeInfo.Id);
  				if (employee != null)
  				{
  				  attachments.Add(employee);
  				  taskText += " - " + employeeInfo.DisplayValue + Environment.NewLine;
  				}
  			}
  			Functions.Module.Remote.SendTask("Необходимо заполнить подразделение у сотрудников", taskText, notificationPerformers, attachments);
  		}
  	}
  	
  	/// <summary>
  	/// Отправить уведомление о том, что подразделение не определено в исходных данных.
  	/// </summary>
  	protected virtual void SendDepartmentUndefinedNotification(List<EmployeeInfo> undefinedDepartmentEmployees)
  	{
  		if (undefinedDepartmentEmployees.Any())
  		{
		  	var notificationPerformers = GetNotificationPerformers();
        var notificationText = "В исходных данных для следующих сотрудников не задано подразделение:" + Environment.NewLine;
        var attachments = new List<IEntity>();
        foreach (var employeeInfo in undefinedDepartmentEmployees)
        {
        	var employee = Functions.Module.Remote.GetEmployeeById(employeeInfo.Id);
        	if (employee != null)
        	{
        	  attachments.Add(employee);
        	  notificationText += " - " + employeeInfo.DisplayValue + Environment.NewLine;
        	}
        }
        Functions.Module.Remote.SendNotice("Ошибки синхронизации подразделений", notificationText, notificationPerformers, attachments);
  		}
  	}
  	
  	#endregion
  	
    #region Публичные функции модуля 
    
    /// <summary>
    /// Выполнить синхронизацию.
    /// </summary>
    [Public]
    public virtual void Sync()
    {
    	var tempFileName = Path.GetTempFileName();
    	try
    	{
    	  Export(tempFileName);
    	  Import(tempFileName);
    	}
    	finally
    	{
    		File.Delete(tempFileName);
    	}
    }
    
    /// <summary>
    /// Выполнить экспорт.
    /// </summary>
    /// <param name="exportFileName">Имя файла экспорта.</param>
    [Public]
  	public virtual void Export(string exportFileName)
  	{
  		var exportEngine = new ExportEngine(exportFileName);
  		exportEngine.Export();
  	}    
  	
  	/// <summary>
  	/// Добавить результаты к соответствующим списокам.
  	/// </summary>
  	/// <param name="credentials">Список сгенерированных учетных данных.</param>
  	/// <param name="unsavedEmployees">Список несохраненных сотрудников.</param>
  	/// <param name="newEmployeesWithoutDepartment">Список новых сотрудников с незаполненным подразделением.</param>
  	/// <param name="undefinedDepartmentEmployees">Список сотрудников, у которых подразделение в исходной системе не заполнено.</param>
  	/// <param name="importResult">Результат импорта.</param>
  	private void AddResults(List<Credential> credentials, List<UnsavedInfo> unsavedEmployees, List<EmployeeInfo> newEmployeesWithoutDepartment, List<EmployeeInfo> undefinedDepartmentEmployees,
      ImportResult importResult)
  	{
			if (importResult != null)
			{
				credentials.AddRange(importResult.Credentials);
				unsavedEmployees.AddRange(importResult.UnsavedEmployees);
				newEmployeesWithoutDepartment.AddRange(importResult.NewEmployeesWithoutDepartment);
				undefinedDepartmentEmployees.AddRange(importResult.UndefinedDepartmentEmployees);
			}  		
  	}
  	
  	/// <summary>
  	/// Создать параметры импорта.
  	/// </summary>
  	/// <param name="sourceJson">Исходный JSON для импорта.</param>
  	/// <returns>Структура с параметрами импорта.</returns>
  	private ImportParams CreateImportParams(string sourceJson)
  	{
  		var importParams = ImportParams.Create();
  		importParams.SourceJson = sourceJson;
  		importParams.AuthenticationType = SettingManager.Instance.AuthenticationType;
  		importParams.NeedGetPersonFullNameFromDisplayName = SettingManager.Instance.NeedGetPersonFullNameFromDisplayName;
  		return importParams;
  	}
  	
  	/// <summary>
  	/// Подготовить значение JSON-файла.
  	/// </summary>
  	/// <param name="value">Строковое значение.</param>
  	/// <returns>Подготовленное для записи в JSON значение.</returns>
  	private string PrepareJsonValue(string value)
  	{
  	  if (string.IsNullOrEmpty(value))
  	    return string.Empty;

  	  return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
  	}
  	
  	/// <summary>
  	/// Записать результирующие логи импорта.
  	/// </summary>
  	/// <param name="importResult">Результат импорта.</param>
  	private void WriteResultLogs(ImportResult importResult)
  	{
  	  const string ChangePropertyExtendedLogMessageFormatString = 
  	    "{{\"operation\": \"Change entity\", " +
  	    "\"id\": \"{0}\", \"type\": \"{1}\", \"displayValue\": \"{2}\", " +
  	    "\"property\": \"{3}\", \"previousValue\": \"{4}\", \"newValue\": \"{5}\"}}";
  	  const string ChangePropertySimpleLogMessageFormatString = 
  	    "{{\"operation\": \"Change entity\", " +
  	    "\"id\": \"{0}\", \"type\": \"{1}\", \"displayValue\": \"{2}\", " +
  	    "\"property\": \"{3}\"}}";  	  
  	  const string CreateEntityLogMessageFormatString = 
  	    "{{\"operation\": \"Create entity\", \"id\": \"{0}\", \"type\": \"{1}\"}}";
  	  
  	  var joinedLogRecords = new List<ISimpleAppliedStructure>();
  	  joinedLogRecords.AddRange(importResult.ChangeRequisiteLogRecords);
  	  joinedLogRecords.AddRange(importResult.CreateEntityLogRecords);
  	  joinedLogRecords = joinedLogRecords.OrderBy(item => 
        {
          if (item is ChangeRequisiteLogRecord)
            return ((ChangeRequisiteLogRecord)item).LogRecordIndex;
          else if (item is CreateEntityLogRecord)
            return ((CreateEntityLogRecord)item).LogRecordIndex;
          else
            return int.MinValue;
        }).ToList();
  	  foreach (var logRecord in joinedLogRecords)
  	  {
  	    if (logRecord is ChangeRequisiteLogRecord)
  	    {
  	      var changeRequisiteLogRecord = (ChangeRequisiteLogRecord)logRecord;
  	      if (SettingManager.Instance.WritePropertyValuesInLogs)
  	      {
  	        ExtendedLogger.InfoFormat(
  	          ChangePropertyExtendedLogMessageFormatString,
  	          changeRequisiteLogRecord.EntityId,
  	          PrepareJsonValue(changeRequisiteLogRecord.EntityTypeName),
  	          PrepareJsonValue(changeRequisiteLogRecord.EntityDisplayValue),
  	          PrepareJsonValue(changeRequisiteLogRecord.PropertyName),
  	          PrepareJsonValue(changeRequisiteLogRecord.CurrentValue),
  	          PrepareJsonValue(changeRequisiteLogRecord.NewValue));
  	      }
  	      else
  	      {
  	        ExtendedLogger.InfoFormat(
  	          ChangePropertySimpleLogMessageFormatString,
  	          changeRequisiteLogRecord.EntityId,
  	          PrepareJsonValue(changeRequisiteLogRecord.EntityTypeName),
  	          PrepareJsonValue(changeRequisiteLogRecord.EntityDisplayValue),
  	          PrepareJsonValue(changeRequisiteLogRecord.PropertyName));
  	      }
  	    }
  	    else if (logRecord is CreateEntityLogRecord)
  	    {
  	      var createEntityLogRecord = (CreateEntityLogRecord)logRecord;
  	      ExtendedLogger.InfoFormat(
  	        CreateEntityLogMessageFormatString,
  	        createEntityLogRecord.EntityId,
  	        PrepareJsonValue(createEntityLogRecord.EntityTypeName));
  	    }
  	  }
  	}
  	
  	/// <summary>
  	/// Выполнить импорт.
  	/// </summary>
  	/// <param name="importFileName">Имя файла импорта.</param>
  	[Public]
  	public virtual void Import(string importFileName)
  	{
      const string ImportStartedMessage = "{\"operation\": \"Import started\"}";
      const string ImportFinishedMessage = "{{\"operation\": \"Import finished\", \"processedCount\": {0}, \"errorCount\": {1}, \"duration\": {2}}}";
  		try
  		{
  			var sourceJsonString = File.ReadAllText(importFileName);
  			var jObjects = JsonConvert.DeserializeObject<List<JObject>>(sourceJsonString);
  			var totalCount = jObjects.Count;
  			var packageSize = SettingManager.Instance.PackageSize;
  			JArray jPackage = new JArray();
  			List<Credential> credentials = new List<Credential>();
  			List<UnsavedInfo> unsavedEmployees = new List<UnsavedInfo>();
  			List<EmployeeInfo> newEmployeesWithoutDepartment = new List<EmployeeInfo>();
  			List<EmployeeInfo> undefinedDepartmentEmployees = new List<EmployeeInfo>();
  			
  		  var stopWatch = new Stopwatch();
  		  stopWatch.Start();
  			try
  			{
  			  ExtendedLogger.Info(ImportStartedMessage);

  			  int index = 0;
  			  while (index < jObjects.Count)
  			  {
  				  jPackage.Add(jObjects[index++]);
  				  if (jPackage.Count == packageSize)
  				  {
  				  	var result = Functions.Module.Remote.Import(CreateImportParams(jPackage.ToString()));
  				  	WriteResultLogs(result);
  					  AddResults(credentials, unsavedEmployees, newEmployeesWithoutDepartment, undefinedDepartmentEmployees, result);
  				   	jPackage = new JArray();
  				  }
  			  }
  			  if (jPackage.Any())
  			  {
  			  	var result = Functions.Module.Remote.Import(CreateImportParams(jPackage.ToString()));
  			  	WriteResultLogs(result);
  				  AddResults(credentials, unsavedEmployees, newEmployeesWithoutDepartment, undefinedDepartmentEmployees, result);
  			  }
  		  
		      SendCredentialNotification(credentials);
		      SendErrorNotification(unsavedEmployees);
		      SendDetermineDepartmentTask(newEmployeesWithoutDepartment);
		      SendDepartmentUndefinedNotification(undefinedDepartmentEmployees);
  			}
  			finally
  			{
  			  stopWatch.Stop();
  				ExtendedLogger.InfoFormat(ImportFinishedMessage, totalCount - unsavedEmployees.Count, unsavedEmployees.Count, stopWatch.ElapsedMilliseconds);
  			}
  		}
  		catch (Exception e)
  		{
  			Logger.Error(e.Message, e);
  			throw;
  		}
  	}
  	
  	/// <summary>
  	/// Проставить значение атрибута у заданных подразделений.
  	/// </summary>
  	/// <param name="parameters">Параметры.</param>
  	[Public]
  	public virtual void FillSyncAttribute(string parameters)
  	{
  		var index = parameters.IndexOf('=');
  		var fillPaths = parameters.Substring(index + 1).Split(';').ToList();
  		var fillEngine = new FillEngine(fillPaths);
  		fillEngine.ExecuteFilling();
  	}
  	
  	#endregion
  }
}
