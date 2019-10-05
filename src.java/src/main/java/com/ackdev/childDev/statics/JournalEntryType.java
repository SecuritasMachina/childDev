package com.ackdev.childDev.statics;

import java.util.Date;

import com.ackdev.common.statics.EnumPropertyType;
import com.fasterxml.jackson.annotation.JsonAutoDetect;
import com.fasterxml.jackson.annotation.JsonIgnore;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.google.appengine.api.datastore.Text;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.Unindex;

import io.swagger.annotations.ApiModel;
import io.swagger.annotations.ApiModelProperty;

@JsonIgnoreProperties(ignoreUnknown = true)

@Entity
@Unindex
@Cache
@JsonAutoDetect
@ApiModel(value = "Account Property", description = "Property object for an account")
public class JournalEntryType {
    @ApiModelProperty(value = "HREF Hash for import", allowableValues = "hash")
    @Index
    public String hrefHash;
    @Index
    @ApiModelProperty(value = "Expiration Date", allowableValues = "empty,past date,future date")
    public Date expirationDate;
    @Index
    @ApiModelProperty(value = "Type of Property", allowableValues = "empty,REALESTATE, CAR, TRUCK, EQUIPMENT, UNDEFINED")
    public EnumPropertyType type;

    @Index
    @Id
    @ApiModelProperty(value = "Unique identifier", allowableValues = "guid")
    public String guid;

    @Index
    @ApiModelProperty(value = "Reference to primary address Property or equipment is located", allowableValues = "guid")
    public String addressGuid_fk;

    public String phoneNumber;
    public String digitalId;
    public String vin;
    public String make;
    public String model;
    public String color;
    public String style;
    public String description;
    public Integer year;
    public Text longDescription;
    public Text notes;
    public Double overAllhours;
    public Double framehours;
    public Double enginehours;
    public Double mileage;

    public String remoteId;

    public Date lastMaintDate = new Date();
    public Date enteredDate = new Date();

    @JsonIgnore
    public Text historyJson;
    public Date updatedOn;
    @Index
    public String accountFk;
    @Index
    public String companyAccountGuidFk;
    public Double msrp;
    public Double currentValue;
    public String tags;
    public Double purchasePrice;
    public Double quantity;
    public Double serviceLevel;

    @OnLoad
    void onLoad() {
    }


}
