#ifndef OHOOKSL_H
#define OHOOKSL_H

typedef GUID SLID;
typedef void *HSLC;

typedef enum _tagSLDATATYPE {
    SL_DATA_NONE        = REG_NONE,
    SL_DATA_SZ          = REG_SZ,
    SL_DATA_DWORD       = REG_DWORD,
    SL_DATA_BINARY      = REG_BINARY,
    SL_DATA_MULTI_SZ    = REG_MULTI_SZ,
    SL_DATA_SUM         = 100,
} SLDATATYPE;

typedef enum _tagSLLICENSINGSTATUS {
    SL_LICENSING_STATUS_UNLICENSED,
    SL_LICENSING_STATUS_LICENSED,
    SL_LICENSING_STATUS_IN_GRACE_PERIOD,
    SL_LICENSING_STATUS_NOTIFICATION,
    SL_LICENSING_STATUS_LAST
} SLLICENSINGSTATUS;

typedef struct _tagSL_LICENSING_STATUS {
    SLID SkuId;
    SLLICENSINGSTATUS eStatus;
    DWORD dwGraceTime;
    DWORD dwTotalGraceDays;
    HRESULT hrReason;
    UINT64 qwValidityExpiration;
} SL_LICENSING_STATUS;

HRESULT WINAPI SLGetLicensingStatusInformation(
    HSLC hSLC,
    SLID *pAppID,
    SLID *pProductSkuId,
    PWSTR pwszRightName,
    UINT *pnStatusCount,
    SL_LICENSING_STATUS **ppLicensingStatus
);

HRESULT WINAPI SLGetProductSkuInformation(
    HSLC hSLC,
    const SLID *pProductSkuId,
    PCWSTR pwszValueName,
    SLDATATYPE *peDataType,
    UINT *pcbValue,
    PBYTE *ppbValue
);

#endif
