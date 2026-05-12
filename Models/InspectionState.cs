using System;
using System.Collections.Generic;
using System.Text;

namespace Camera_Insepctor_Project.Models
{
    // 검사 결과에 대한 상태 열거형
    // Ok : 정상 상태
    // Warning : 검사 결과의 문제
    // NoObject : 객체가 감지되지 않음
    // Error : 프로그램 처리 자체의 실패

    internal enum InspectionState
    {
        Ok,
        Warning,
        NoObject,
        Error,
        CalibrationRequired
    }
}
