package com.ackdev.childDev.bean;

import static com.ackdev.common.application.CommonOfyService.ofyCommon;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.ackdev.childDev.jdo.GoalItemProgress;
import com.ackdev.common.dto.CommonResponse;
import com.ackdev.store.UserStore;

@Component
public class GoalProgressBean {

	@Autowired
	private UserStore mUserStore;

	public CommonResponse.Status delete(String guid) {
		CommonResponse.Status ret = null;
		GoalItemProgress GoalItemProgress = this.get(guid);
		if (GoalItemProgress.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			ofyCommon().delete().entity(GoalItemProgress).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = guid;
		} else {
			// TODO log spoofing attempt
			ret = new CommonResponse.Status().fail();
		}
		return ret;
	}

	public GoalItemProgress get(String guid) {
		GoalItemProgress ret = ofyCommon().load().type(GoalItemProgress.class).id(guid).now();
		if (ret.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			return ret;
		} else {
			// TODO log spoofing attempt
		}
		return null;
	}

	public CommonResponse.Status update(GoalItemProgress GoalItemProgress) {
		CommonResponse.Status ret = null;
		if (GoalItemProgress.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			ofyCommon().save().entity(GoalItemProgress).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = GoalItemProgress.guid;

		} else {
			// TODO log spoofing attempt
			ret = new CommonResponse.Status().fail();
		}

		return ret;
	}

	public Collection<CommonResponse.Status> update(Collection<GoalItemProgress> journalCollection) {
		ArrayList<CommonResponse.Status> ret = new ArrayList<>();
		for (GoalItemProgress j : journalCollection) {
			CommonResponse.Status e = this.update(j);
			ret.add(e);
		}
		return ret;
	}

	public Collection<GoalItemProgress> getRange(Long startTS, Long endTS) {
		List<GoalItemProgress> eList = ofyCommon().load().type(GoalItemProgress.class)
				.filter("accountFk", mUserStore.getUserAccount().guid).filter("enteredDate >=", startTS)
				.filter("enteredDate <=", endTS).list();// .first()
		return eList;
	}

	public void UT_InjectUserStore(UserStore userStore) {
		this.mUserStore = userStore;
	}

}
