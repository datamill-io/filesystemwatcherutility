
use your_favorite_database
go

drop TABLE [dbo].[MN_File_System_Mgmt]

go

CREATE TABLE [dbo].[MN_File_System_Mgmt](

        [id] int not null IDENTITY,

        [directory] [varchar](255) NULL,

        [file_name] [varchar](255) NULL,

        [file_extension] [varchar](32) NULL,

        [change_type] [varchar](32) NULL,

        [create_date] [datetime2](3) NULL,

constraint PK_MN_File_System_Mgmt_id primary key (id)

) ON [PRIMARY]

GO

 

create index IX_MN_File_System_Mgmt_file_extension on MN_File_System_Mgmt (file_extension)
go
create index IX_MN_File_System_Mgmt_change_type on MN_File_System_Mgmt (change_type)
go
create index IX_MN_File_System_Mgmt_create_date on MN_File_System_Mgmt (create_date)
go 