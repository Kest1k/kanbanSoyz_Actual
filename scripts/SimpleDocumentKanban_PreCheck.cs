// =========================================================================
//  Скрипт-обработчик для шаблона  InfoObjects\BASIC\Documents\SimpleDocumentKanban
//  Назначение: скрыть канбан-вложения из поиска у тех, кому недоступна
//              задача-владелец. Видимость файла полностью повторяет
//              видимость самой задачи - со всей иерархией (assignee, creator,
//              начальник сектора, начальник отдела, приватные и т.д.).
//
//  Куда вставлять:
//      Конфигуратор → ветка "Шаблоны → Информационные объекты"
//          → InfoObjects\BASIC\Documents\SimpleDocumentKanban
//          → вкладка "Скрипты"
//      Скопировать тело метода PreCheckOperation целиком.
//
//  Обязательное условие:
//      На шаблоне должен быть атрибут с ключом "OwnerTaskKey" (тип: строка).
//      Заводится один раз вручную в конфигураторе. Заполняется автоматически
//      при создании документа из канбана (см. DoAttachLocalFile в
//      SOYUZ_UPLOAD_KanbanScreen_script.cs).
//
//  Логика:
//      • Не для операций видимости (View/Load/Read) - return null (стандарт)
//      • Админам - return null
//      • Если OwnerTaskKey не задан (legacy объекты) - return null
//      • Если задача не найдена - return false (документ-сирота скрыт)
//      • Если пользователь видит задачу (CheckOperation ViewObject) - return null
//      • Иначе - return false
//
//  Главный смысл: переадресуем проверку прав на задачу. Всю иерархию
//  (роли, сектор, отдел, приватность) система рассчитает сама - мы её не
//  переизобретаем.
//
//  Производительность:
//      Один SearchOperation по NameKey задачи + один user.CheckOperation
//      на проверку. Обе операции лёгкие, индексированные.
// =========================================================================

public override bool? PreCheckOperation(
    User                user,
    InfoObject          obj,
    AttributableObject  target,
    OperationIdentifier op,
    String              attrNameKey,
    CollectionElement   element )
{
    try
    {
        // 1. Анонимные/системные сессии - доверяем стандартной схеме
        if( user == null ) return null;

        // 2. Реагируем только на операции видимости
        if( op != OperationIdentifier.LoadObjectToCache
         && op != OperationIdentifier.ViewObject
         && op != OperationIdentifier.ReadFile )
            return null;

        // 3. Админам - без проверок
        try { if( user.IsAdministrator ) return null; } catch { }

        // 4. Читаем ключ задачи-владельца
        string ownerKey = null;
        try { ownerKey = obj.GetString( "OwnerTaskKey" ); } catch { }
        if( string.IsNullOrEmpty( ownerKey ) ) return null; // legacy без привязки

        // 5. Ищем задачу по NameKey
        InfoObject task = null;
        try
        {
            var searchOp = new SearchOperation( EntityIdentifier.InfoObject, "KbSdkAcl" );
            var filter   = new SearchInfoObjectFilterItem();
            filter.PropertyId             = SearchConditionFilterItem.SystemPropertyId.NameKey;
            filter.OperatorId             = RelationalOperator.Equal;
            filter.Argument1              = ownerKey;
            searchOp.SearchExpressionTree = filter;
            searchOp.MaxSQLResultsCount   = 3;
            searchOp.MaxResultsCount      = 3;
            searchOp.Execute( false );

            if( searchOp.FoundObjects != null )
            {
                foreach( var o in searchOp.FoundObjects.OfType<InfoObject>() )
                {
                    var k = string.IsNullOrEmpty( o.NameKey ) ? o.Id.ToString() : o.NameKey;
                    if( k == ownerKey ) { task = o; break; }
                }
            }
        }
        catch { }
        if( task == null ) return false; // задача удалена - документ-сирота скрыт

        // 6. Переадресуем видимость на задачу. Если пользователь видит задачу
        //    (через assignee/creator/иерархию/роль) - значит, видит и документ.
        try
        {
            if( user.CheckOperation( task, OperationIdentifier.ViewObject, null ) )
                return null;
        }
        catch { return null; } // не смогли проверить - не блокируем

        // 7. Задачу не видит - документ тоже скрыт
        return false;
    }
    catch
    {
        // На любую неожиданную ошибку - доверяем стандартной схеме,
        // чтобы не сломать UI глобально.
        return null;
    }
}
