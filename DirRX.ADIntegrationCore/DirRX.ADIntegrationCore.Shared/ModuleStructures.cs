using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace DirRX.ADIntegrationCore.Structures.Module
{
	/// <summary>
	/// Структура, содержащая информацию о сотруднике.
	/// </summary>
	partial class EmployeeInfo
	{
		/// <summary>
		/// ИД сотрудника.
		/// </summary>
		public int Id { get; set; }
		/// <summary>
		/// Отображаемое значение сотрудника.
		/// </summary>
		public string DisplayValue { get; set; }
	}
	
	/// <summary>
	/// Структура, содержащая информацию о несохраненной сущности.
	/// </summary>
	partial class UnsavedInfo
	{
		/// <summary>
		/// ИД сущности.
		/// </summary>
		public int Id { get; set; }
		/// <summary>
		/// Отображаемое значение.
		/// </summary>
		public string DisplayValue { get; set; }
		/// <summary>
		/// Сообщение об ошибке.
		/// </summary>
		public string ErrorMessage { get; set; }
	}
	
	/// <summary>
	/// Структура, содержащая информацию об учетных данных.
	/// </summary>
	partial class Credential
	{
		/// <summary>
		/// ИД сотрудника.
		/// </summary>
		public int EmployeeId { get; set; }
		/// <summary>
		/// Логин пользователя.
		/// </summary>
		public string LoginName { get; set; }
		/// <summary>
		/// Пароль пользователя.
		/// </summary>
		public string Password { get; set; }
	}
	
  /// <summary>
  /// Структура, содержащая результат импорта.
  /// </summary>
  partial class ImportResult
  {
  	/// <summary>
  	/// Список сотрудников без подразделения.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.EmployeeInfo> NewEmployeesWithoutDepartment { get; set; }
  	
  	/// <summary>
  	/// Список существующих сотрудников, у которых в исходной системе подразделение не определено.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.EmployeeInfo> UndefinedDepartmentEmployees { get; set; }
  	
  	/// <summary>
  	/// Список с информацией о несохраненных сущностях.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.UnsavedInfo> UnsavedEmployees { get; set; }
  	
  	/// <summary>
  	/// Список с информацией о созданных учетных данных.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.Credential> Credentials { get; set; }
  	
  	/// <summary>
  	/// Список структур для записи в лог-файл информации об изменении значений реквизитов.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.ChangeRequisiteLogRecord> ChangeRequisiteLogRecords { get; set; }
  	
  	/// <summary>
  	/// Список структур для записи в лог-файл информации о создании сущностей.
  	/// </summary>
  	public List<DirRX.ADIntegrationCore.Structures.Module.CreateEntityLogRecord> CreateEntityLogRecords { get; set; }
  }
  
  /// <summary>
  /// Структура, содержащая параметры импорта.
  /// </summary>
  partial class ImportParams
  {
  	/// <summary>
  	/// Исходный JSON для импорта.
  	/// </summary>
  	public string SourceJson { get; set; }
  	
  	/// <summary>
  	/// Тип аутентификации.
  	/// </summary>
  	public string AuthenticationType { get; set; }
  	
  	/// <summary>
  	/// Признак необходимости заполнять ФИО персоны на основе атрибута displayName.
  	/// </summary>
  	public bool NeedGetPersonFullNameFromDisplayName { get; set; }
  }
  

  /// <summary>
  /// Результат разбора полного имени логина.
  /// </summary>
  partial class SplitFullLoginNameResult
  {
  	/// <summary>
  	/// Имя домена.
  	/// </summary>
  	public string DomainName { get; set; }
  	
  	/// <summary>
  	/// Имя логина.
  	/// </summary>
  	public string LoginName { get; set; }
  }  
  
  /// <summary>
  /// Запись лога об изменении значения реквизита.
  /// </summary>
  partial class ChangeRequisiteLogRecord
  {
    /// <summary>
    /// Порядковый номер записи в лог.
    /// </summary>
    public int LogRecordIndex { get; set; }
    
    /// <summary>
    /// Название типа сущности.
    /// </summary>
    public string EntityTypeName { get; set; }
    
    /// <summary>
    /// ИД сущности.
    /// </summary>
    public int EntityId { get; set; }
    
    /// <summary>
    /// Отображаемое значение сущности.
    /// </summary>
    public string EntityDisplayValue { get; set; }
    
    /// <summary>
    /// Наименование свойства.
    /// </summary>
    public string PropertyName { get; set; }
    
    /// <summary>
    /// Текущее значение свойства.
    /// </summary>
    public string CurrentValue { get; set; }
    
    /// <summary>
    /// Новое значение свойства.
    /// </summary>
    public string NewValue { get; set; }
  }
  
  /// <summary>
  /// Запись лога о создании сущности.
  /// </summary>
  partial class CreateEntityLogRecord
  {
    /// <summary>
    /// Порядковый номер записи в лог.
    /// </summary>
    public int LogRecordIndex { get; set; }
    
    /// <summary>
    /// Название типа сущности.
    /// </summary>
    public string EntityTypeName { get; set; }
    
    /// <summary>
    /// ИД сущности.
    /// </summary>
    public int EntityId { get; set; }
  }
}