using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.Data.Migrations
{
    /// <summary>위치 태그 매핑에 노드 좌표(PoseX/PoseY/PoseAngle) 추가 — 시뮬레이션 도착 pose 발행용</summary>
    public partial class AddNodePose : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PoseX",
                table: "LocationTagMappings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PoseY",
                table: "LocationTagMappings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PoseAngle",
                table: "LocationTagMappings",
                type: "REAL",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PoseX", table: "LocationTagMappings");
            migrationBuilder.DropColumn(name: "PoseY", table: "LocationTagMappings");
            migrationBuilder.DropColumn(name: "PoseAngle", table: "LocationTagMappings");
        }
    }
}
