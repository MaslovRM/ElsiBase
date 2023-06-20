using ADServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DirRX.ADIntegrationCore.Structures.Module;
using CommonLibrary;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.CoreEntities.Login;
using Sungero.CoreEntities.Shared;
using Sungero.Domain.Shared;
using Sungero.Parties;
using Sungero.Workflow;

namespace DirRX.ADIntegrationCore.Server
{
  /// <summary>
  /// Класс с серверными функциями.
  /// </summary>
  public class ModuleFunctions
  {
    /// <summary>
    /// Получить сотрудника по ИД.
    /// </summary>
    /// <param name="id">ИД сотрудника.</param>
    /// <returns>Сотрудник.</returns>
    [Remote]
    public IEmployee GetEmployeeById(int id)
    {
      IEmployee result = null;
      try
      {
        result = Employees.Get(id);
      }
      catch (Exception e)
      {
        Logger.Error(e.Message, e);
      }
      return result;
    }
    
    /// <summary>
    /// Найти сотрудника по логину.
    /// </summary>
    /// <param name="loginName">Имя логина.</param>
    /// <returns>Сотрудник.</returns>
    [Remote]
    public IEmployee GetEmployeeByLoginName(string loginName)
    {
      return Employees.GetAll().FirstOrDefault(e => (e.Login != null) && (e.Login.LoginName == loginName));
    }
    
    /// <summary>
    /// Отправить уведомление.
    /// </summary>
    /// <param name="subject">Тема.</param>
    /// <param name="text">Текст.</param>
    /// <param name="performers">Получатели.</param>
    /// <param name="attachments">Вложения.</param>
    [Remote]
    public void SendNotice(string subject, string text, List<IRecipient> performers, List<IEntity> attachments)
    {
      var attachmentsArray = attachments != null ? attachments.ToArray() : null;
      var task = Sungero.Workflow.SimpleTasks.CreateWithNotices(subject, performers, attachmentsArray);
      task.ActiveText = text;
      task.Save();
      task.Start();
    }
    
    /// <summary>
    /// Отправить задачу.
    /// </summary>
    /// <param name="subject">Тема.</param>
    /// <param name="text">Текст.</param>
    /// <param name="performers">Получатели.</param>
    /// <param name="attachments">Вложения.</param>
    [Remote]
    public void SendTask(string subject, string text, List<IRecipient> performers, List<IEntity> attachments)
    {
      var task = Sungero.Workflow.SimpleTasks.Create(subject, performers.ToArray());
      foreach (var attachment in attachments)
        task.Attachments.Add(attachment);
      task.ActiveText = text;
      task.Save();
      task.Start();
    }
    
    /// <summary>
    /// Получить список пользователей для уведомления.
    /// </summary>
    /// <param name="roleName">Имя роли.</param>
    /// <returns>Список пользователей.</returns>
    [Remote]
    public List<IRecipient> GetNotificationPerformers(string roleName)
    {
      var performers = new List<IRecipient>();
      var roles = Roles.GetAll().Where(r => r.Name == roleName);
      performers.AddRange(roles);
      return performers;
    }
    
    #region Пайплайн импорта 
    
    /// <summary>
    /// Добавить в список измененных сотрудников.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="employee">Сотрудник.</param>
    protected void AddToChangedEmployees(ImportContext importContext, IEmployee employee)
    {
      if (!importContext.ChangedEmployees.Contains(employee))
        importContext.ChangedEmployees.Add(employee);
    }
    
    /// <summary>
    /// Отключить поверку заполненности обязательных свойств.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    private void DisableRequiredPropertyCheck(IEmployee employee)
    {
      // TODO: Можно попробовать переделать на рефлексию.
      employee.State.Properties.Department.IsRequired = false;
      employee.State.Properties.Email.IsRequired = false;
      employee.State.Properties.Person.IsRequired = false;
      employee.State.Properties.Status.IsRequired = false;
    }
    
    /// <summary>
    /// Сравнить строки.
    /// </summary>
    /// <param name="firstParam">Параметр 1.</param>
    /// <param name="secondParam">Парамтер 2.</param>
    /// <returns>true, если строки равны, иначе - false.</returns>
    private static bool StringEquals(string firstParam, string secondParam)
    {
      if (string.IsNullOrEmpty(firstParam) && string.IsNullOrEmpty(secondParam))
        return true;
      if (string.IsNullOrEmpty(firstParam) || string.IsNullOrEmpty(secondParam))
        return false;
      return string.Equals(firstParam.Trim(), secondParam.Trim(), StringComparison.InvariantCultureIgnoreCase);
    }
    
    /// <summary>
    /// Получить свойство объекта по имени.
    /// </summary>
    /// <param name="sourceObject">Исходный объект.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <returns>Свойство объекта.</returns>
    private System.Reflection.PropertyInfo GetObjectPropertyByName(object sourceObject, string propertyName)
    {
      return sourceObject.GetType().GetProperties().Where(p => p.Name == propertyName).First();
    }
    
    /// <summary>
    /// Получить свойство объекта по имени и типу.
    /// </summary>
    /// <param name="sourceObject">Исходный объект.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="objectType">Тип объекта.</param>
    /// <returns>Свойство объекта.</returns>
    private System.Reflection.PropertyInfo GetObjectPropertyByNameAndType(object sourceObject, string propertyName, Type objectType)
    {
      return sourceObject.GetType().GetProperties().Where(p => p.Name == propertyName && p.PropertyType.GetTypeGuid() == objectType.GetTypeGuid()).First();
    }
    
    /// <summary>
    /// Добавить запись в лог-файл об изменении значения свойства.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="newValue">Новое значение.</param>
    /// <param name="currentValue">Текущее значение.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="entity">Сущность.</param>
    protected void AppendLogRecordAboutRequisiteChange(ImportContext importContext, object newValue, object currentValue, string propertyName, IEntity entity)
    {
      var logRecord = ChangeRequisiteLogRecord.Create();
      logRecord.LogRecordIndex = importContext.LogRecords.Count;
      logRecord.EntityId = entity.Id;
      logRecord.EntityTypeName = GetEntityTypeName(entity);
      logRecord.EntityDisplayValue = entity.DisplayValue;
      logRecord.PropertyName = propertyName;
      logRecord.CurrentValue = currentValue == null ? string.Empty : currentValue.ToString();
      logRecord.NewValue = newValue == null ? string.Empty : newValue.ToString();
      importContext.LogRecords.Add(logRecord);
    }
    
    /// <summary>
    /// Добавить запись в лог-файл о создании сущности.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="entity">Сущность.</param>
    protected void AppendLogRecordAboutCreatedEntity(ImportContext importContext, IEntity entity)
    {
      var logRecord = CreateEntityLogRecord.Create();
      logRecord.LogRecordIndex = importContext.LogRecords.Count;
      logRecord.EntityId = entity.Id;
      logRecord.EntityTypeName = GetEntityTypeName(entity);
      importContext.LogRecords.Add(logRecord);
    }
    
    /// <summary>
    /// Преобразовать сущность в строку.
    /// </summary>
    /// <param name="entity">Сущность.</param>
    /// <returns>Строковое представление сущности.</returns>
    protected string EntityToString(IEntity entity)
    {
      if (entity != null)
        return string.Format(
          "{0} \"{1}\" with id \"{2}\"", GetEntityTypeName(entity), entity.DisplayValue, entity.Id);
      else
        return string.Empty;
    }
    
    /// <summary>
    /// Получить название типа сущности.
    /// </summary>
    /// <param name="entity">Сущность.</param>
    /// <returns>Название типа сущности.</returns>
    protected string GetEntityTypeName(IEntity entity)
    {
      return entity.GetEntityMetadata().GetDisplayName().ToString(new CultureInfo("en-US"));
    }
        
    /// <summary>
    /// Обновить строковый реквизит.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="newValue">Новое значение.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="entity">Сущность.</param>    
    /// <returns>Истина, если свойство действительно было обновлено.</returns>
    protected bool UpdateStringRequisite(ImportContext importContext, string newValue, string propertyName, IEntity entity)
    {
      var property = GetObjectPropertyByName(entity, propertyName);
      var currentValue = (string)property.GetValue(entity, null);
      if (StringEquals(newValue, currentValue))
        return false;
      AppendLogRecordAboutRequisiteChange(importContext, newValue, currentValue, propertyName, entity);
      property.SetValue(entity, newValue);
      return true;
    }
    
    /// <summary>
    /// Обновить реквизит с перечислением.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="newValue">Новое значение.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="entity">Сущность.</param>
    /// <returns>Истина, если свойство было обновлено.</returns>
    protected bool UpdateEnumRequisite(ImportContext importContext, Nullable<Enumeration> newValue, string propertyName, IEntity entity)
    {
      var property = GetObjectPropertyByName(entity, propertyName);
      var currentValue = (Nullable<Enumeration>)property.GetValue(entity, null);
      if (newValue == currentValue)
        return false;
      AppendLogRecordAboutRequisiteChange(importContext, newValue, currentValue, propertyName, entity);
      property.SetValue(entity, newValue);
      return true;
    }
    
    /// <summary>
    /// Обновить реквизит со ссылкой на сущность.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="newValue">Новое значение.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="entity">Сущность.</param>
    /// <returns>Истина, если свойство было обновлено.</returns>
    protected bool UpdateEntityRequisite(ImportContext importContext, IEntity newValue, string propertyName, IEntity entity)
    {
      System.Reflection.PropertyInfo property = null;
      
      // При наличии перекрытий может существовать несколько свойств с одинаковым именем,
      // в этом случае выбираем для присвоения свойство, объявленное на самом базовом слое.
      if (newValue != null)
        property = GetObjectPropertyByNameAndType(entity, propertyName, newValue.GetType().GetOriginalType());
      else
        property = GetObjectPropertyByName(entity, propertyName);
      
      var currentValue = (IEntity)property.GetValue(entity, null);
      if (newValue == null && currentValue == null)
        return false;
      if (newValue != null && currentValue != null && newValue.Id == currentValue.Id)
        return false;
      var newValueString = EntityToString(newValue);
      var currentValueString = EntityToString(currentValue);
      AppendLogRecordAboutRequisiteChange(importContext, newValueString, currentValueString, propertyName, entity);
      property.SetValue(entity, newValue);
      return true;
    }
    
    #region Пайплайн импорта. Синхронизация персоны
    
    /// <summary>
    /// Получить ExternalLink на персону.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <returns>ExternalLink на персону.</returns>
    private IExternalLink GetPersonExternalLink(ImportContext importContext, JObject jEmployee)
    {
      return ExternalLinkFactory.GetOrCreateExternalLink(
        importContext, ExternalLinkTypeConstant.UserActiveDirectoryType, (string)jEmployee["ExternalID"], typeof(IPerson).GetTypeGuid());
    }
    
    /// <summary>
    /// Получить или создать персону.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="externalLink">ExternalLink на персону.</param>
    /// <param name="isCreated">Признак того, что персона была создана.</param>
    /// <returns>Персона.</returns>
    private IPerson GetOrCreatePerson(ImportContext importContext, IExternalLink externalLink, out bool isCreated)
    {
      isCreated = false;
      var person = People.GetAll().SingleOrDefault(e => e.Id == externalLink.EntityId);
      if (person == null)
      {
        person = People.Create();
        AppendLogRecordAboutCreatedEntity(importContext, person);
        isCreated = true;
      }

      return person;
    }
    
    /// <summary>
    /// Обновить персону.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="person">Персона.</param>
    /// <param name="externalLink">ExternalLink.</param>
    /// <param name="jEmployee">Словарь с реквизитами.</param>
    /// <param name="changedExternalLinks">Список измененных ExternalLink-ов.</param>
    protected virtual void UpdatePersonProperties(ImportContext importContext, IPerson person, IExternalLink externalLink, JObject jEmployee, List<IExternalLink> changedExternalLinks)
    {
      externalLink.SetLinkTo(person);
      changedExternalLinks.Add(externalLink);
      
      string firstName;
      string lastName;
      string middleName;
      if (importContext.NeedGetPersonFullNameFromDisplayName)
      {
        var personFullName = Padeg.SplitPersonFullName((string)jEmployee["DisplayNameForExtractingPersonFullName"]);
        firstName = personFullName.FirstName;
        lastName = personFullName.LastName;
        middleName = personFullName.MiddleName;
      }
      else
      {
        firstName = (string)jEmployee["FirstName"];
        lastName = (string)jEmployee["LastName"];
        middleName = (string)jEmployee["MiddleName"];
      }
      
      UpdateStringRequisite(importContext, firstName, "FirstName", person);
      UpdateStringRequisite(importContext, lastName, "LastName", person);
      UpdateStringRequisite(importContext, middleName, "MiddleName", person);
      UpdateStringRequisite(importContext, (string)jEmployee["PersonPhones"], "Phones", person);
      UpdateStringRequisite(importContext, (string)jEmployee["WebAddress"], "Homepage", person);
      UpdateStringRequisite(importContext, (string)jEmployee["Address"], "LegalAddress", person);
    }
    
    /// <summary>
    /// Обновить персону.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <param name="employee">Сотрудник.</param>
    /// <param name="changedExternalLinks">Список измененных ExternalLink-овю</param>
    protected virtual void UpdatePerson(ImportContext importContext, JObject jEmployee, IEmployee employee, List<IExternalLink> changedExternalLinks)
    {
      var personExternalLink = GetPersonExternalLink(importContext, jEmployee);
      
      bool isCreated;
      var person = GetOrCreatePerson(importContext, personExternalLink, out isCreated);
      UpdatePersonProperties(importContext, person, personExternalLink, jEmployee, changedExternalLinks);
        
      if (employee.Person == null || employee.Person.Id != person.Id || isCreated || person.State.IsChanged)
      {
        employee.Person = person;  
        AddToChangedEmployees(importContext, employee);
      }
    }
    
    #endregion
    
    #region Пайплайн импорта. Синхронизация логина
    
    /// <summary>
    /// Получить полное имя логина.
    /// </summary>
    /// <param name="domainName">Имя домена.</param>
    /// <param name="loginName">Имя логина.</param>
    /// <param name="authenticationType">Тип аутентификации.</param>
    /// <returns>Полное имя логина.</returns>
    private string GetFullLoginName(string domainName, string loginName, string authenticationType)
    {
      if (authenticationType == AuthenticationTypeConstants.Windows && !string.IsNullOrEmpty(domainName))
        return domainName + "\\" + loginName;
      else
        return loginName;
    }
    
    /// <summary>
    /// Разделить полное имя логина на домен и имя логина.
    /// </summary>
    /// <param name="fullLoginName">Полное имя логина.</param>
    /// <returns>Массив строк, состоящий из домена и имени логина.</returns>
    private SplitFullLoginNameResult SplitFullLoginName(string fullLoginName)
    {
      var result = SplitFullLoginNameResult.Create();
      var parts = fullLoginName.Split('\\');
      if (parts.Length == 1)
      {
        result.DomainName = string.Empty;
        result.LoginName = parts[0];
      }
      else if (parts.Length >= 2)
      {
        result.DomainName = parts[0];
        result.LoginName = parts[1];
      }
      return result;
    }

    /// <summary>
    /// Получить или создать логин.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="employee">Сотрудник.</param> 
    /// <param name="fullLoginName">Полное имя логина.</param>
    /// <param name="isCreated">Выходной признак того, что логин был создан.</param>
    /// <returns>Логин.</returns>
    private ILogin GetOrCreateLogin(ImportContext importContext, IEmployee employee, string fullLoginName, out bool isCreated)
    {
      isCreated = false;
      ILogin resultLogin;
      resultLogin = Logins.GetAll().SingleOrDefault(l => l.LoginName == fullLoginName);
      if (resultLogin == null)
      {
        if (employee.Login != null)
          resultLogin = employee.Login;
        else
        {
          resultLogin = Logins.Create();
          AppendLogRecordAboutCreatedEntity(importContext, resultLogin);
          isCreated = true;
        }
      }

      return resultLogin;
    }
    
    /// <summary>
    /// Обновить свойства логина.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="login">Логин.</param>
    /// <param name="newDomainName">Новое имя домена.</param>
    /// <param name="newLoginName">Новое имя логина.</param>
    /// <param name="newAuthenticationType">Новый тип аутентификации.</param>
    protected virtual void UpdateLoginProperties(ImportContext importContext, ILogin login, string newDomainName, string newLoginName, string newAuthenticationType)
    {
      var currentAuthenticationType = login.TypeAuthentication == TypeAuthentication.Windows ? AuthenticationTypeConstants.Windows : AuthenticationTypeConstants.Password;
      string fullLoginName = string.Empty;
      if (login.State.IsInserted)
        fullLoginName = GetFullLoginName(newDomainName, newLoginName, newAuthenticationType);
      else
      {
        var splitResult = SplitFullLoginName(login.LoginName);
        fullLoginName = GetFullLoginName(splitResult.DomainName, newLoginName, currentAuthenticationType);
      }
      if (login.LoginName != fullLoginName)
        UpdateStringRequisite(importContext, fullLoginName, "LoginName", login);

      var newAuthenticationTypeEnum = importContext.AuthenticationType == AuthenticationTypeConstants.Windows ? TypeAuthentication.Windows : TypeAuthentication.Password;
      if (login.State.IsInserted && newAuthenticationTypeEnum != login.TypeAuthentication)
        UpdateEnumRequisite(importContext, newAuthenticationTypeEnum, "TypeAuthentication", login);
    }
    
    /// <summary>
    /// Обработать создание логина.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="employee">Сотрудник.</param>
    /// <param name="login">Логин.</param>
    /// <param name="importResult">Результат импорта.</param>
    protected virtual void HandleLoginCreated(ImportContext importContext, IEmployee employee, ILogin login, ImportResult importResult)
    {
      string password = "";
      if (importContext.AuthenticationType == AuthenticationTypeConstants.Password)
      {
        login.NeedChangePassword = true;
        LoginExtension.CreateNewPassword(login, out password);
      }
      importResult.Credentials.Add(new Credential() 
      {
        EmployeeId = employee.Id,
        LoginName = login.LoginName,
        Password = password
      });
    }
    
    /// <summary>
    /// Обновить логин.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <param name="employee">Сотрудник.</param>
    /// <param name="importResult">Результат импорта.</param>
    protected virtual void UpdateLogin(ImportContext importContext, JObject jEmployee, IEmployee employee, ImportResult importResult)
    {
      bool isCreated;
      
      var fullLoginName = GetFullLoginName(
        (string)jEmployee["NetBIOSDomainName"], 
        (string)jEmployee["LoginName"],
       importContext.AuthenticationType);
      
      var login = GetOrCreateLogin(
        importContext,
        employee,
        fullLoginName,
        out isCreated);
      UpdateLoginProperties(
        importContext,
        login,
        (string)jEmployee["NetBIOSDomainName"], 
        (string)jEmployee["LoginName"],
        importContext.AuthenticationType);
      
      if (isCreated)
        HandleLoginCreated(importContext, employee, login, importResult);
      
      if (employee.Login == null || employee.Login.Id != login.Id || isCreated || login.State.IsChanged)
      {
        employee.Login = login;
        AddToChangedEmployees(importContext, employee);
      }
    }
    
    #endregion
    
    #region Пайплайн импорта. Синхронизация должности
    
    /// <summary>
    /// Создать или получить должность.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jobTitleName">Строка с наименованием должности.</param>
    /// <param name="isCreated">Признак того, что должность была создана.</param>
    /// <returns>Должность.</returns>
    private IJobTitle GetOrCreateJobTitle(ImportContext importContext, string jobTitleName, out bool isCreated)
    {
      isCreated = false;
      var jobTitle = JobTitles.GetAll().SingleOrDefault(j => string.Equals(j.Name, jobTitleName, StringComparison.InvariantCultureIgnoreCase));
      if (jobTitle == null)
      {
        jobTitle = JobTitles.Create();
        AppendLogRecordAboutCreatedEntity(importContext, jobTitle);
        isCreated = true;
      }

      return jobTitle;
    }
    
    /// <summary>
    /// Обновить свойства должности.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jobTitle">Должность.</param>
    /// <param name="jobTitleName">Наименование должности.</param>
    protected virtual void UpdateJobTitleProperties(ImportContext importContext, IJobTitle jobTitle, string jobTitleName)
    {
      UpdateStringRequisite(importContext, jobTitleName, "Name", jobTitle);
    }
    
    /// <summary>
    /// Обновить должность.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <param name="employee">Сотрудник.</param>
    protected virtual void UpdateJobTitle(ImportContext importContext, JObject jEmployee, IEmployee employee)
    {
      var jobTitleName = (string)jEmployee["JobTitle"];
      if (!string.IsNullOrEmpty(jobTitleName))
      {
        bool isCreated;
        var jobTitle = GetOrCreateJobTitle(importContext, jobTitleName, out isCreated);
        UpdateJobTitleProperties(importContext, jobTitle, jobTitleName);
        
        if (employee.JobTitle == null || jobTitle.Id != employee.JobTitle.Id || isCreated)
        {
          UpdateEntityRequisite(importContext, jobTitle, "JobTitle", employee);
          AddToChangedEmployees(importContext, employee);
        }
      }
      else if (employee.JobTitle != null)
      {
        UpdateEntityRequisite(importContext, null, "JobTitle", employee);
        AddToChangedEmployees(importContext, employee);
      }
    }
    
    #endregion
    
    #region Пайплайн импорта. Синхронизация подразделения
    
    /// <summary>
    /// Обработать переезд сотрудника в другое подразделение.
    /// </summary>
    /// <param name="employee">Запись сотрудника.</param>
    /// <param name="jEmployee">Свойства сотрудника из входного файла.</param>
    protected virtual void HandleEmployeeDepartmentChanged(IEmployee employee, JObject jEmployee)
    {
    }
    
    /// <summary>
    /// Обновить подразделение.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <param name="employee">Сотрудник.</param>
    /// <param name="isCreated">Признак созданного сотрудника.</param>
    /// <param name="importResult">Результат импорта.</param>
    protected virtual void UpdateDepartment(ImportContext importContext, JObject jEmployee, IEmployee employee, bool isCreated, ImportResult importResult)
    {
      var departmentName = (string)jEmployee["Department"];
      if (!string.IsNullOrEmpty(departmentName))
      {
        var departments = Departments.GetAll().Where(d => d.Name == departmentName);
        if (departments.Count() == 1)
        {
          var department = departments.First();
          if (employee.Department == null || employee.Department.Id != department.Id)
          {
            UpdateEntityRequisite(importContext, department, "Department", employee);
            if (!employee.State.IsInserted)
              HandleEmployeeDepartmentChanged(employee, jEmployee);
            AddToChangedEmployees(importContext, employee);
          }
        }
        else if (departments.Count() == 0)
          throw new Exception(string.Format(
            "Department with name \"{0}\" not found in system. Department wouldn't be changed for employee with ID {1} and name {2}.",
              departmentName, employee.Id, employee.DisplayValue));
        else
          throw new Exception(string.Format(
            "There are multiple departments with name \"{0}\" found in system. Department wouldn't be changed for employee with ID {1} and name {2}.",
              departmentName, employee.Id, employee.DisplayValue));
      }
      else
      {
        if (employee.State.IsInserted)
        {
          UpdateEntityRequisite(importContext, null, "Department", employee);
          UpdateEnumRequisite(importContext, new Enumeration("Closed"), "Status", employee);
          AddToChangedEmployees(importContext, employee);
          importResult.NewEmployeesWithoutDepartment.Add(new EmployeeInfo() 
          { 
            Id = employee.Id, 
            DisplayValue = employee.DisplayValue 
          });
        }    
        else if (employee.Department != null)
          importResult.UndefinedDepartmentEmployees.Add(new EmployeeInfo()
          {
            Id = employee.Id,
            DisplayValue = employee.DisplayValue
          });
      }
    }
    
    #endregion
    
    /// <summary>
    /// Обновить электронную почту.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Информация о сотруднике, из которой выполняется синхронизация.</param>
    /// <param name="employee">Запись сотрудника, в которую выполняется синхронизация.</param>    
    protected virtual void UpdateEmail(ImportContext importContext, JObject jEmployee, IEmployee employee)
    {
      if (UpdateStringRequisite(importContext, (string)jEmployee["Email"], "Email", employee))
        AddToChangedEmployees(importContext, employee);
      
      if (string.IsNullOrEmpty(employee.Email))
      {
        employee.NeedNotifyExpiredAssignments = false;
        employee.NeedNotifyNewAssignments = false;
      }
    }
    
    /// <summary>
    /// Обработать событие на открытие записи сотрудника.
    /// </summary>
    /// <param name="employee">Запись сотрудника.</param>
    /// <param name="jEmployee">Информация о сотруднике из входного файла.</param>
    protected virtual void HandleEmployeeOpened(IEmployee employee, JObject jEmployee)
    {
    }
    
    /// <summary>
    /// Обработать событие на закрытие записи сотрудника.
    /// </summary>
    /// <param name="employee">Запись сотрудников.</param>
    /// <param name="jEmployee">Информация о сотруднике из входного файла.</param>
    protected virtual void HandleEmployeeClosed(IEmployee employee, JObject jEmployee)
    {
    }
    
    /// <summary>
    /// Проверить, заполнены ли обязательные реквизиты у сотрудника.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>true если заполнены обязательные реквизиты, иначе false.</returns>
    protected virtual bool IsFillRequiredProperty(IEmployee employee)
    {
      return employee.Person != null && employee.Department != null;
    }

    /// <summary>
    /// Обновить изменение статуса сотрудника.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Информация о сотруднике, из которой выполняется синхронизация.</param>
    /// <param name="employee">Запись сотрудника, в которую выполняется синхронизация.</param>
    protected virtual void UpdateStatus(ImportContext importContext, JObject jEmployee, IEmployee employee)
    {
      var status = (string)jEmployee["Status"];
      if (StringEquals(status, "Closed"))
      {
        if (!employee.Status.Value.Equals(new Enumeration("Closed")))
        {
          UpdateEnumRequisite(importContext, new Enumeration("Closed"), "Status", employee);
          HandleEmployeeClosed(employee, jEmployee);
          AddToChangedEmployees(importContext, employee);
        }
      }
      else
      {
        if (!employee.Status.Value.Equals(new Enumeration("Active")) && IsFillRequiredProperty(employee))
        {
          UpdateEnumRequisite(importContext, new Enumeration("Active"), "Status", employee);
          HandleEmployeeOpened(employee, jEmployee);
          AddToChangedEmployees(importContext, employee);
        }
      }
    }
    
    /// <summary>
    /// Закрыть созданного сотрудника, если не заполнены обязательные свойства.
    /// </summary>
    /// <param name="employee">Запись сотрудника.</param>
    /// <param name="importContext">Контекст импорта.</param>
    protected virtual void CloseEmployeeIfNeeded(ImportContext importContext, IEmployee employee)
    {
      if (employee.State.IsInserted && !IsFillRequiredProperty(employee))
      {
        if (!employee.Status.Value.Equals(new Enumeration("Closed")))
        {
          UpdateEnumRequisite(importContext, new Enumeration("Closed"), "Status", employee);
          AddToChangedEmployees(importContext, employee);
        }
      }
    }
    
    /// Обработать событие на создание сотрудника.
    /// </summary>
    /// <param name="employee">Запись сотрудника.</param>
    /// <param name="jEmployee">Информация сотрудника, из которой выполняется синхронизация.</param>
    protected virtual void HandleEmployeeCreated(IEmployee employee, JObject jEmployee)
    {
    }
    
    /// <summary>
    /// Обработать событие на изменение сотрудника.
    /// </summary>
    /// <param name="employee">Запись сотрудника.</param>
    /// <param name="jEmployee">Информация сотрудника, из которой выполняется синхронизация.</param>
    protected virtual void HandleEmployeeChanged(IEmployee employee, JObject jEmployee)
    {
    }
    
    /// <summary>
    /// Обновить свойства сотрудника.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Информация о сотрудника, из которой выполняется синхронизация.</param>
    /// <param name="employee">Запись сотрудника, в которую выполняется синхронизация.</param>
    /// <param name="isCreated">Признак того, что запись сотрудника создана.</param>
    /// <param name="changedExternalLinks">Список измененных ExternalLink-ов.</param>
    /// <param name="importResult">Результат импорта.</param>
    protected virtual void UpdateEmployeeProperties(ImportContext importContext, JObject jEmployee, IEmployee employee, bool isCreated, List<IExternalLink> changedExternalLinks, ImportResult importResult)
    {
      UpdateLogin(importContext, jEmployee, employee, importResult);
      UpdatePerson(importContext, jEmployee, employee, changedExternalLinks);
      UpdateJobTitle(importContext, jEmployee, employee);
      
      if (UpdateStringRequisite(importContext, (string)jEmployee["EmployeePhones"], "Phone", employee))
        AddToChangedEmployees(importContext, employee);

      UpdateDepartment(importContext, jEmployee, employee, isCreated, importResult);
      UpdateEmail(importContext, jEmployee, employee);
      UpdateStatus(importContext, jEmployee, employee);
      DisableRequiredPropertyCheck(employee);    
      CloseEmployeeIfNeeded(importContext, employee);

      if (employee.State.IsInserted)
        HandleEmployeeCreated(employee, jEmployee);
      else if (employee.State.IsChanged)
        HandleEmployeeChanged(employee, jEmployee);
    }
    
    /// <summary>
    /// Получить или создать сотрудника.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь со свойствами сотрудника.</param>
    /// <param name="isCreated">Признак того, что сотружник был создан.</param>
    /// <param name="changedExternalLinks">Список измененных ExternalLink-ов.</param>
    /// <returns>Сущность сотрудника.</returns>
    protected virtual IEmployee GetOrCreateEmployee(ImportContext importContext, JObject jEmployee, out bool isCreated, List<IExternalLink> changedExternalLinks)
    {
      isCreated = false;
      var externalLink = ExternalLinkFactory.GetOrCreateExternalLink(
        importContext,
        ExternalLinkTypeConstant.UserActiveDirectoryType,
        (string)jEmployee["ExternalID"],
        typeof(IEmployee).GetTypeGuid());
            
      var employee = Employees.GetAll().SingleOrDefault(e => e.Id == externalLink.EntityId);
      if (employee == null)
      {
        employee = Employees.Create();
        AppendLogRecordAboutCreatedEntity(importContext, employee);
        isCreated = true;
      }
      
      externalLink.SetLinkTo(employee);
      changedExternalLinks.Add(externalLink);
      
      return employee;
    }
    
    #endregion
    
    /// <summary>
    /// Создать результат импорта.
    /// </summary>
    /// <returns>Результат импорта.</returns>
    private ImportResult CreateImportResult()
    {
      var result = new ImportResult();
      result.NewEmployeesWithoutDepartment = new List<EmployeeInfo>();
      result.UndefinedDepartmentEmployees = new List<EmployeeInfo>();
      result.UnsavedEmployees = new List<UnsavedInfo>();
      result.Credentials = new List<Credential>();
      result.ChangeRequisiteLogRecords = new List<ChangeRequisiteLogRecord>();
      result.CreateEntityLogRecords = new List<CreateEntityLogRecord>();
      return result;
    }
    
    /// <summary>
    /// Сохранить внешние ссылки.
    /// </summary>
    /// <param name="externalLinks">Список ExternalLink-ов.</param>
    private void SaveLinks(List<IExternalLink> externalLinks)
    {
      if (externalLinks.Any())
        Sungero.Domain.Shared.RemoteFunctionExecutor.Execute(
          Guid.Parse("270E2543-55A3-4473-9A3A-5F76EBF9BB88"), "SaveLinks", externalLinks);
    }
    
    /// <summary>
    /// Сохранить изменения.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="jEmployee">Словарь с информацией о сотруднике.</param>
    /// <param name="employee">Сущность сотрудника, которую надо сохранить.</param>
    protected virtual void SaveChanges(ImportContext importContext, JObject jEmployee, IEmployee employee)
    {
      if (importContext.ChangedEmployees.Contains(employee))
      {
        using (var session = Sungero.Domain.Session.CreateIndependentSession())
        {
          try
          {
            session.Update(employee);
            List<IEntity> additionalEntities;
            if (importContext.AdditionalEntities.TryGetValue(employee, out additionalEntities))
              foreach (var entity in additionalEntities)
                session.Update(entity);
            session.SubmitChanges();
          }
          finally
          {
            // Очистить сессию, чтобы в следующий раз не делались повторные попытки сохранения уже сохраненных сущностей.
            Sungero.Domain.Session.ContextualInnerSession.Clear();
          }
        }
      }
    }

    /// <summary>
    /// Обработать ошибку импорта.
    /// </summary>
    /// <param name="e">Исключение.</param>
    /// <param name="employeeEntity">Сотрудник, при обработке которого возникла ошибка.</param>
    /// <param name="result">Результат импорта</param>
    private void HandleImportError(Exception e, IEmployee employeeEntity, ImportResult result)
    {
      if (employeeEntity != null)
      {
        var employeeInfo = result.NewEmployeesWithoutDepartment.FirstOrDefault(i => i.Id == employeeEntity.Id);
        if (employeeInfo != null)
          result.NewEmployeesWithoutDepartment.Remove(employeeInfo);
        employeeInfo = result.UndefinedDepartmentEmployees.FirstOrDefault(i => i.Id == employeeEntity.Id);
        if (employeeInfo != null)
          result.UndefinedDepartmentEmployees.Remove(employeeInfo);
        var credential = result.Credentials.FirstOrDefault(c => c.EmployeeId == employeeEntity.Id);
        if (credential != null)
          result.Credentials.Remove(credential);
        result.UnsavedEmployees.Add(new UnsavedInfo() 
        {
          Id = employeeEntity.Id,
          DisplayValue = employeeEntity.DisplayValue,
          ErrorMessage = e.Message
        });
        Logger.ErrorFormat("Can't save entity with id {0} and name {1}. Reason: {2}.", e, employeeEntity.Id, employeeEntity.DisplayValue, e.Message);
      }
      else
        Logger.Error(e.Message, e);
    }
    
    /// <summary>
    /// Скопировать записи лог-файла в результат.
    /// </summary>
    /// <param name="importContext">Контекст импорта.</param>
    /// <param name="importResult">Результат импорта.</param>
    private void CopyLogRecordsToResult(ImportContext importContext, ImportResult importResult)
    {
      foreach (var logRecord in importContext.LogRecords)
      {
        if (logRecord is ChangeRequisiteLogRecord)
          importResult.ChangeRequisiteLogRecords.Add((ChangeRequisiteLogRecord)logRecord);
        else if (logRecord is CreateEntityLogRecord)
          importResult.CreateEntityLogRecords.Add((CreateEntityLogRecord)logRecord);
      }
      importContext.LogRecords.Clear();
    }
    
    /// <summary>
    /// Импортировать данные по сотрудникам из переданного JSON.
    /// </summary>
    /// <param name="importParams">Параметры импорта.</param>
    [Remote]
    public virtual ImportResult Import(ImportParams importParams)
    {
      ImportResult result = null;
      const string ImportStartedMessage = "{ \"operation\": \"Import started\" }";
      const string ImportFinishedMessage = "{{ \"operation\": \"Import finished\", \"SavedCount\": {0}, \"ErrorCount\": {1}}}";
      try
      {
        result = CreateImportResult();
        var employees = JsonConvert.DeserializeObject<List<JObject>>(importParams.SourceJson);
      
        var importContext = ImportContext.Create();
        importContext.AuthenticationType = importParams.AuthenticationType;
        importContext.NeedGetPersonFullNameFromDisplayName = importParams.NeedGetPersonFullNameFromDisplayName;
      
        ExtendedLogger.Info(ImportStartedMessage);
        int errorCount = 0;
        try
        { 
          foreach (var employee in employees)
          {
            var changedExternalLinks = new List<IExternalLink>();
            IEmployee employeeEntity = null;
            bool isCreated = false;
            bool isError = false;
            try
            {
              employeeEntity = GetOrCreateEmployee(importContext, employee, out isCreated, changedExternalLinks);
              UpdateEmployeeProperties(importContext, employee, employeeEntity, isCreated, changedExternalLinks, result);
              SaveChanges(importContext, employee, employeeEntity);
            }
            catch (Exception e)
            {
              isError = true;
              errorCount++;
              HandleImportError(e, employeeEntity, result);
            }
            // Сохраняться будут только те ExternalLink-и, которые соответствуют сущностям, сохранившимся без ошибок.
            if (!isError)
              importContext.ChangedExternalLinks.AddRange(changedExternalLinks);
          }
          CopyLogRecordsToResult(importContext, result);
          // Очистить сессию, чтобы при сохранении ссылок не делались повторные попытки сохранения уже сохраненных сущностей.
          Sungero.Domain.Session.ContextualInnerSession.Clear();
          SaveLinks(importContext.ChangedExternalLinks);
        }
        finally
        {
          ExtendedLogger.InfoFormat(ImportFinishedMessage, employees.Count - errorCount, errorCount);
        }
      }
      catch (Exception e)
      {
        Logger.Error(e.Message, e);
        throw;
      }
      return result;
    }
  }
}