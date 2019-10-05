package com.ackdev.jdo.common;

import java.util.Date;

import com.fasterxml.jackson.annotation.JsonAutoDetect;
import com.fasterxml.jackson.annotation.JsonIgnore;
import com.google.appengine.api.datastore.Text;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Ignore;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.Unindex;

import io.swagger.annotations.ApiModel;

@Entity
@Unindex
@Cache
@JsonAutoDetect
@ApiModel(value = "Feedback Item", description = "Feedback for other entities such as Job and maps")

public class FeedbackItem {

    @Id
    public Long id;

    @Index
    public String itemGuid;

    public EnumFeedbackTypes itemType = EnumFeedbackTypes.STOREITEM;
    @Ignore
    public String uiItemType = EnumFeedbackTypes.STOREITEM.name();
    @Ignore
    public Long uiEnteredDate;

    public String name;

    @JsonIgnore
    public Text comment;

    @JsonIgnore
    @Index
    public Date enteredDate = new Date();

    @Ignore
    public String uiComment;

    @OnLoad
    void onLoad() { /* do something after load */
        uiItemType = itemType.name();
        uiEnteredDate = enteredDate.getTime();

    }

    public enum EnumFeedbackTypes {

        STOREITEM, MAPITEM
    }
}
