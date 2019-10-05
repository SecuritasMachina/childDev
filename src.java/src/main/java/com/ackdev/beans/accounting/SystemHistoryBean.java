package com.ackdev.beans.accounting;

import static com.ackdev.childDev.application.LocalOfyService.ofy;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.logging.Logger;

import com.ackdev.common.utility.MyUtility;
import com.ackdev.jdo.common.EnumCountCategory;
import com.ackdev.jdo.common.SystemHistory;

public class SystemHistoryBean {
    private static Logger LOG = Logger.getLogger(SystemHistoryBean.class.getName());
    private volatile static SystemHistoryBean mInstance;

    public static SystemHistoryBean getInstance() {
        if (mInstance == null) {
            synchronized (SystemHistoryBean.class) {

                if (mInstance == null) {
                    mInstance = new SystemHistoryBean();
                }
            }
        }
        return mInstance;
    }


    public void log(EnumCountCategory p, Integer pValue) {
        ofy().save()
                .entity(new SystemHistory(p, pValue));

    }

    public void log(EnumCountCategory p, Double pValue) {
        ofy().save()
                .entity(new SystemHistory(p, pValue));

    }

    public void log(String p, String companyAccountGuidFk, String guid, Long priority) {
        ofy().save()
                .entity(new SystemHistory(p, companyAccountGuidFk, guid, priority));
    }

    public void addToSummaryHash(HashMap<String, String> ret, String pPropName, String pGuid) {
        String eVal = ret.get(pPropName);
        if (eVal == null)
            eVal = "0";

        Double existingVal = new Double(eVal);
        if (existingVal == null)
            existingVal = 0.0d;
        Double n = 0.0d;

        if (pGuid != null) {
            n = existingVal + 1.0d;
            ret.put(pPropName, "" + n.intValue());

            String gList = ret.get("L_" + pPropName);
            List aList;
            if (gList == null)
                aList = new ArrayList();
            else
                aList = MyUtility.getGsonObj().fromJson(gList, List.class);

            aList.add(pGuid);

            ret.put("L_" + pPropName, MyUtility.getGsonObj().toJson(aList));
        }
    }


    public void log(EnumCountCategory billNotice, String companyAccountGuidFk, String pMsg) {
        ofy().save()
                .entity(new SystemHistory(billNotice.name(), companyAccountGuidFk, pMsg));

    }
}

