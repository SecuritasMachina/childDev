package com.ackdev.jdo.common;

import java.util.Date;

import com.ackdev.common.utility.MyUtility;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.OnSave;

import io.swagger.annotations.ApiModel;

@Entity
@Index
@Cache
@ApiModel(value = "System History", description = "Internal system audit log")
public class SystemHistory {
    //    @Id
    public Long id;

    @Id
    @Index
    public String guid;

    @Index
    public String category;
    @Index
    public Date createdOn = new Date();
    @Index
    public Double value = new Double(0);
    @Index
    public Integer intValue = new Integer(0);

    public String stringVal;
    @Index
    public String guid1Fk;
    @Index
    public String guid2Fk;

    public SystemHistory() {
    }

    public SystemHistory(EnumCountCategory pCategory, Integer pValue) {
        this.guid = MyUtility.makeUUID();
        this.category = pCategory.name();
        this.intValue = pValue;
        this.createdOn = new Date();
    }

    public SystemHistory(EnumCountCategory pCategory, Double pValue) {
        this.guid = MyUtility.makeUUID();
        this.category = pCategory.name();
        this.value = pValue;
        this.createdOn = new Date();
    }

    public SystemHistory(EnumCountCategory pCategory, String pGuid1Fk, String pGuid2Fk, Long pValue) {
        this(pCategory.name(), pGuid1Fk, pGuid2Fk, pValue);
    }

    public SystemHistory(String pCategory, String pGuid1Fk, String pGuid2Fk, Long pValue) {
        this.guid = MyUtility.makeUUID();
        this.category = pCategory;
        this.guid1Fk = pGuid1Fk;
        this.guid2Fk = pGuid2Fk;
        if (pValue != null)
            this.intValue = pValue.intValue();

        this.createdOn = new Date();

    }

    public SystemHistory(String pCategory, String pGuid1Fk, String pMsg) {
        this.guid = MyUtility.makeUUID();
        this.category = pCategory;
        this.guid1Fk = pGuid1Fk;
        this.stringVal = pMsg;
        this.createdOn = new Date();

    }

    @OnLoad
    void onLoad() { /* do something after load */
    }

    @OnSave
    void onPersist() { /* do something before persisting */

    }

}
