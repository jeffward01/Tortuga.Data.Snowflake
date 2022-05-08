﻿/*
 * Copyright (c) 2012-2019 Snowflake Computing Inc. All rights reserved.
 */

namespace Tortuga.Data.Snowflake.Core.ResponseProcessing;

internal static class ResultSetUtil
{
	internal static int CalculateUpdateCount(this SFBaseResultSet resultSet)
	{
		SFResultSetMetaData metaData = resultSet.sfResultSetMetaData;
		SFStatementType statementType = metaData.statementType;

		long updateCount = 0;
		switch (statementType)
		{
			case SFStatementType.INSERT:
			case SFStatementType.UPDATE:
			case SFStatementType.DELETE:
			case SFStatementType.MERGE:
			case SFStatementType.MULTI_INSERT:
				resultSet.Next();
				for (int i = 0; i < resultSet.columnCount; i++)
				{
					updateCount += resultSet.GetValue<long>(i);
				}

				break;

			case SFStatementType.COPY:
				var index = resultSet.sfResultSetMetaData.getColumnIndexByName("rows_loaded");
				if (index >= 0)
				{
					resultSet.Next();
					updateCount = resultSet.GetValue<long>(index);
					resultSet.Rewind();
				}
				break;

			case SFStatementType.COPY_UNLOAD:
				var rowIndex = resultSet.sfResultSetMetaData.getColumnIndexByName("rows_unloaded");
				if (rowIndex >= 0)
				{
					resultSet.Next();
					updateCount = resultSet.GetValue<long>(rowIndex);
					resultSet.Rewind();
				}
				break;

			case SFStatementType.SELECT:
				updateCount = -1;
				break;

			default:
				updateCount = 0;
				break;
		}

		if (updateCount > int.MaxValue)
			return -1;

		return (int)updateCount;
	}
}