package com.ackdev.childDev.jdo;

import com.ackdev.childDev.statics.JournalEntryType;
import com.ackdev.common.utility.MyUtility;
import com.fasterxml.jackson.annotation.JsonAutoDetect;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.google.appengine.api.datastore.Text;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.OnSave;
import com.googlecode.objectify.annotation.Unindex;

import io.swagger.annotations.ApiModel;
import io.swagger.annotations.ApiModelProperty;

@JsonIgnoreProperties(ignoreUnknown = true)

@Entity
@Unindex
@Cache
@JsonAutoDetect
@ApiModel(value = "Journal Entry", description = "Point in time journal entry")
public class JournalItem {
    @Index
    @Id
    @ApiModelProperty(value = "Unique identifier", allowableValues = "guid")
    public String guid;
    
    @Index
    public String accountFk;

	
    @Index
    @ApiModelProperty(value = "Expiration Long", allowableValues = "empty,past date,future date")
    public Long expirationDate;
	
	@Index
    public Long enteredDate = System.currentTimeMillis();

	@Index
    public JournalEntryType type;
    
    public Text notes;
    
    public Long updatedOn;
    
    
    public String locationText;
    
    public String activity;
    
    public String tags;
    //TBD
    public String mood;
    
    @OnLoad
    void onLoad() {
    }
	@OnSave
	void onPersist() { /* do something before persisting */
		updatedOn = System.currentTimeMillis();
		if (guid == null)
			guid = MyUtility.makeUUID();

	}
}
